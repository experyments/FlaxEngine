#include "QuadSphere.h"
#include "Engine/Content/Content.h"
#include "Engine/Content/Assets/Model.h"
#include "Engine/Core/Log.h"
#include "Engine/Core/Collections/Dictionary.h"
#include "Engine/Core/Collections/HashSet.h"
#include "Engine/Core/Collections/Sorting.h"
#include "Engine/Core/Math/Color.h"
#include "Engine/Core/Math/Color32.h"
#include "Engine/Engine/EngineService.h"
#include "Engine/Engine/Engine.h"
#include "Engine/Engine/Time.h"
#include "Engine/Foliage/Foliage.h"
#if USE_EDITOR
#include "Engine/Graphics/RenderTask.h"
#endif
#include "Engine/Graphics/Models/MeshAccessor.h"
#include "Engine/Graphics/Shaders/GPUVertexLayout.h"
#include "Engine/Level/Actors/StaticModel.h"
#include "Engine/Level/Actors/Camera.h"
#include "Engine/Level/Prefabs/PrefabManager.h"
#include "Engine/Level/SceneQuery.h"
#include "Engine/Physics/CollisionData.h"
#include "Engine/Physics/Colliders/BoxCollider.h"
#include "Engine/Physics/Colliders/MeshCollider.h"
#include "Engine/Platform/CPUInfo.h"
#include "Engine/Profiler/ProfilerCPU.h"
#include "Engine/Scripting/Scripting.h"
#include "Engine/Serialization/Serialization.h"
#include "Engine/Threading/Task.h"
#include "Engine/Threading/Threading.h"
#include <atomic>
#include <algorithm>
#include <cmath>
#include <memory>
#include <queue>
#include <set>
#include <vector>

namespace
{
    static bool TryGetEditorViewPosition(Vector3& position)
    {
#if USE_EDITOR
        if (!Engine::IsPlayMode())
        {
            ScopeLock lock(RenderTask::TasksLocker);
            for (auto* task : RenderTask::Tasks)
            {
                if (!task || task == MainRenderTask::Instance || !task->Enabled || task->FrameCount <= 0 ||
                    task->Order != 1 || task->LastUsedFrame > Engine::FrameCount || Engine::FrameCount - task->LastUsedFrame > 2)
                    continue;
                auto* sceneTask = ScriptingObject::Cast<SceneRenderTask>(task);
                if (!sceneTask || sceneTask->ActorsSource != ActorsSources::Scenes || sceneTask->Camera.Get() || sceneTask->View.IsOfflinePass)
                    continue;
                position = sceneTask->View.Origin + Vector3(sceneTask->View.Position);
                if (!position.IsNanOrInfinity())
                    return true;
            }
        }
#endif
        return false;
    }

    static double FoliageSpawnDistance(const QuadSphereActor& actor, const QuadSphereFoliageConfig& config)
    {
        if (config.SpawnDistance != -1.0)
            return config.SpawnDistance;
#if USE_EDITOR
        if (!Engine::IsPlayMode() && actor.PreviewFoliageInEditor && actor.FoliageSpawnDistance == 0.0)
            return actor.EditorFoliagePreviewDistance;
#endif
        return actor.FoliageSpawnDistance;
    }

    static bool TryGetFoliageTarget(const QuadSphereActor& actor, Vector3& position)
    {
        if (!Engine::IsPlayMode() && TryGetEditorViewPosition(position))
            return true;
        Actor* target = !Engine::IsPlayMode() ? actor.LodTarget.Get() : nullptr;
        if (!target)
            target = Camera::GetMainCamera();
        if (!target)
            return false;
        position = target->GetPosition();
        return true;
    }

    struct QuadSphereBrushData
    {
        Guid Id;
        int32 Order = 0;
        QuadSphereBrushMode Mode = QuadSphereBrushMode::None;
        QuadSphereBrushShape Shape = QuadSphereBrushShape::Circle;
        QuadSphereBrushBiome BiomeOverride = QuadSphereBrushBiome::None;
        Vector3 Position = Vector3::Zero;
        Vector3 AxisX = Vector3::Zero;
        Vector3 AxisY = Vector3::Zero;
        Vector3 AxisNormal = Vector3::Zero;
        float Radius = 0.0f;
        float Falloff = 0.0f;
        float Depth = 0.0f;
        double FlattenHeight = 0.0;
    };

    struct QuadSphereFoliageCellKey
    {
        int32 Cell;
        int32 Entry;

        bool operator==(const QuadSphereFoliageCellKey& other) const
        {
            return Cell == other.Cell && Entry == other.Entry;
        }

        friend uint32 GetHash(const QuadSphereFoliageCellKey& key)
        {
            return ((uint32)key.Cell * 16777619u) ^ (uint32)key.Entry;
        }
    };

    struct QuadSphereFoliageCell
    {
        Array<Transform> Placements;
        ScriptingObjectReference<Foliage> Renderer;
        int32 Attempt = 0;
        int32 AttemptCount = -1;
        int32 Installed = 0;
        int32 Published = 0;
        double LastPublishedTime = 0.0;
        bool GenerationComplete = false;
        bool Complete = false;
    };

    struct QuadSphereFoliageColliderKey
    {
        QuadSphereFoliageCellKey Cell;
        int32 Index;

        bool operator==(const QuadSphereFoliageColliderKey& other) const
        {
            return Cell == other.Cell && Index == other.Index;
        }

        friend uint32 GetHash(const QuadSphereFoliageColliderKey& key)
        {
            return (GetHash(key.Cell) * 16777619u) ^ (uint32)key.Index;
        }
    };

    struct QuadSphereFoliageCollider
    {
        ScriptingObjectReference<BoxCollider> Actor;
    };

    struct QuadSphereFoliageCollisionCandidate
    {
        QuadSphereFoliageColliderKey Key;
        Transform Placement;
        double Distance;

        bool operator<(const QuadSphereFoliageCollisionCandidate& other) const
        {
            if (Distance != other.Distance)
                return Distance < other.Distance;
            if (Key.Cell.Cell != other.Key.Cell.Cell)
                return Key.Cell.Cell < other.Key.Cell.Cell;
            return Key.Index < other.Key.Index;
        }
    };

    struct QuadSpherePrefabCandidate
    {
        Vector3 Surface = Vector3::Zero;
        float Yaw = 0.0f;
        int32 Entry = 0;
        ScriptingObjectReference<Actor> Instance;
        Guid InstanceId = Guid::Empty;
        bool SpawnAttempted = false;
    };

    struct QuadSpherePrefabChunk
    {
        Array<QuadSpherePrefabCandidate> Candidates;
        int32 Entry = 0;
        int32 Attempt = 0;
        int32 AttemptCount = -1;
    };

    struct QuadSphereFoliageSelection
    {
        double Distance;
        int32 Cell;
        int32 Entry;

        bool operator<(const QuadSphereFoliageSelection& other) const
        {
            if (Distance != other.Distance)
                return Distance < other.Distance;
            if (Cell != other.Cell)
                return Cell < other.Cell;
            return Entry < other.Entry;
        }
    };

    struct QuadSphereFoliageRegion
    {
        double LowerBound;
        double MaxDistance;
        int32 Face;
        int32 Entry;
        int32 MinX;
        int32 MaxX;
        int32 MinY;
        int32 MaxY;

        bool operator<(const QuadSphereFoliageRegion& other) const
        {
            return LowerBound > other.LowerBound;
        }
    };

    struct QuadSphereBrushInfluence
    {
        const QuadSphereBrushData* Brush;
        const Transform* PlanetTransform;
    };

    struct QuadSphereTerrainSnapshot
    {
        double Radius = 0.0;
        double BaseElevationBound = 0.0;
        float SkirtDepth = 0.0f;
        float NoiseHeight = 0.0f;
        float NoiseFrequency = 0.0f;
        float UvRebaseRepeat = 0.0f;
        int32 Resolution = 0;
        int32 ChunksPerPatch = 0;
        int32 NoiseOctaves = 0;
        int32 NoiseSeed = 0;
        bool SimpleSphere = false;
        bool RetainSurfaceVertices = false;
        int32 BiomeLayerCount = 0;
        QuadSphereBiomeLayer BiomeLayers[4];
        float BiomeBlendWidth = 0.0f;
        double WaterHeight = 0.0;
        double BeachWidth = 0.0;
        float BeachSmoothing = 0.0f;
        Array<QuadSphereBrushData> Brushes;
        Transform PlanetTransform = Transform::Identity;
    };

    struct QuadSpherePatchKey
    {
        int32 Face;
        int32 Level;
        int32 X;
        int32 Y;

        bool operator==(const QuadSpherePatchKey& other) const
        {
            return Face == other.Face && Level == other.Level && X == other.X && Y == other.Y;
        }

        friend uint32 GetHash(const QuadSpherePatchKey& key)
        {
            uint32 hash = 2166136261u;
            hash = (hash ^ (uint32)key.Face) * 16777619u;
            hash = (hash ^ (uint32)key.Level) * 16777619u;
            hash = (hash ^ (uint32)key.X) * 16777619u;
            return (hash ^ (uint32)key.Y) * 16777619u;
        }
    };

    struct QuadSpherePatchGeometry
    {
        QuadSpherePatchKey Key;
        Double3 Origin;
        Array<Float3> Vertices;
        Array<Float3> Normals;
        Array<Float3> Tangents;
        Array<Float2> UVs;
        Array<Color32> BiomeWeights;
        Array<int32> Indices;
    };

    enum class QuadSphereCookState
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Aborted,
    };

    struct QuadSphereCollisionJob
    {
        std::atomic<QuadSphereCookState> State{QuadSphereCookState::Queued};
        CollisionData* Collision = nullptr;
        QuadSpherePatchGeometry Geometry;
        bool Detached = false;
    };

    class QuadSphereCollisionService : public EngineService
    {
        Array<std::shared_ptr<QuadSphereCollisionJob>> _jobs;
        bool _exiting = false;

        static void ReleaseAsset(QuadSphereCollisionJob& job)
        {
            job.Geometry.Vertices = Array<Float3>();
            job.Geometry.Indices = Array<int32>();
            if (job.Collision)
            {
                job.Collision->RemoveReference();
                job.Collision->DeleteObject();
                job.Collision = nullptr;
            }
        }

    public:
        QuadSphereCollisionService()
            : EngineService(TEXT("Quad Sphere Collision Jobs"), 1)
        {
        }

        bool Init() override
        {
            _exiting = false;
            return false;
        }

        bool IsAcceptingJobs() const
        {
            return !_exiting;
        }

        void Enqueue(const std::shared_ptr<QuadSphereCollisionJob>& job);

        void Update() override
        {
            for (int32 i = _jobs.Count() - 1; i >= 0; i--)
            {
                auto& job = *_jobs[i];
                const auto state = job.State.load(std::memory_order_acquire);
                if (job.Detached && state != QuadSphereCookState::Queued && state != QuadSphereCookState::Running)
                {
                    ReleaseAsset(job);
                    _jobs.RemoveAt(i);
                }
            }
        }

        void BeforeExit() override
        {
            // Stop queued jobs before the thread pool exits and drain running cooks before physics is disposed.
            _exiting = true;
            for (const auto& job : _jobs)
            {
                auto expected = QuadSphereCookState::Queued;
                job->State.compare_exchange_strong(expected, QuadSphereCookState::Aborted, std::memory_order_acq_rel);
            }
            for (const auto& job : _jobs)
            {
                while (job->State.load(std::memory_order_acquire) == QuadSphereCookState::Running)
                    Platform::Sleep(1);
                ReleaseAsset(*job);
            }
            _jobs.Clear();
        }
    };

    QuadSphereCollisionService QuadSphereCollisionJobs;

    struct QuadSpherePatchResources
    {
        QuadSpherePatchKey Key;
        Double3 Origin = Double3::Zero;
        Array<Float3> Vertices;
        ScriptingObjectReference<StaticModel> Renderer;
        ScriptingObjectReference<MeshCollider> Collider;
        AssetReference<Model> MeshModel;
        AssetReference<CollisionData> Collision;
        std::shared_ptr<QuadSphereCollisionJob> Cooking;
        bool CollisionAssigned = false;
    };

    struct QuadSpherePatchInstallation
    {
        // The returned patch remains inactive until the LOD transition is committed.
        static bool Create(QuadSphereActor& planet, const QuadSpherePatchGeometry& geometry, bool generateCollision,
                           bool retainSurfaceVertices, QuadSpherePatchResources& output)
        {
            if (!IsInMainThread())
            {
                LOG(Error, "Quad sphere patch installation must run on the main thread.");
                return false;
            }
            if (output.Renderer.Get() || output.Collider.Get() || output.MeshModel.Get() || output.Collision.Get() || output.Cooking)
            {
                LOG(Error, "Cannot install a quad sphere patch into resources that are already in use.");
                return false;
            }
#if !COMPILE_WITH_PHYSICS_COOKING
            if (generateCollision)
            {
                LOG(Error, "Quad sphere collision cooking is not available in this build.");
                return false;
            }
#endif
            const int32 count = geometry.Vertices.Count();
            if (count == 0 || geometry.Indices.IsEmpty() || geometry.Indices.Count() % 3 != 0 ||
                geometry.Normals.Count() != count || geometry.Tangents.Count() != count ||
                geometry.UVs.Count() != count || geometry.BiomeWeights.Count() != count)
            {
                LOG(Error, "Cannot install a quad sphere patch with inconsistent mesh data.");
                return false;
            }
            for (const int32 index : geometry.Indices)
            {
                if (index < 0 || index >= count)
                {
                    LOG(Error, "Cannot install a quad sphere patch with an invalid mesh index.");
                    return false;
                }
            }

            Model* model = Content::CreateVirtualAsset<Model>();
            if (!model)
            {
                LOG(Error, "Failed to create a quad sphere patch model.");
                return false;
            }
            const int32 meshCount = 1;
            if (model->SetupLODs(ToSpan(&meshCount, 1)))
            {
                LOG(Error, "Failed to initialize a quad sphere patch model.");
                model->DeleteObject();
                return false;
            }

            MeshAccessor accessor;
            GPUVertexLayout* positions = GPUVertexLayout::Get({
                { VertexElement::Types::Position, 0, 0, 0, PixelFormat::R32G32B32_Float },
            });
            GPUVertexLayout* attributes = GPUVertexLayout::Get({
                { VertexElement::Types::Normal, 1, 0, 0, PixelFormat::R10G10B10A2_UNorm },
                { VertexElement::Types::Tangent, 1, 0, 0, PixelFormat::R10G10B10A2_UNorm },
                { VertexElement::Types::TexCoord, 1, 0, 0, PixelFormat::R32G32_Float },
            });
            GPUVertexLayout* colors = GPUVertexLayout::Get({
                { VertexElement::Types::Color, 2, 0, 0, PixelFormat::R8G8B8A8_UNorm },
            });
            if (!positions || !attributes || !colors ||
                accessor.AllocateBuffer(MeshBufferType::Index, geometry.Indices.Count(), PixelFormat::R32_UInt) ||
                accessor.AllocateBuffer(MeshBufferType::Vertex0, count, positions) ||
                accessor.AllocateBuffer(MeshBufferType::Vertex1, count, attributes) ||
                accessor.AllocateBuffer(MeshBufferType::Vertex2, count, colors))
            {
                LOG(Error, "Failed to allocate quad sphere patch mesh buffers.");
                model->DeleteObject();
                return false;
            }

            accessor.Index().SetLinear(geometry.Indices.Get());
            accessor.Position().Set(ToSpan(geometry.Vertices));
            accessor.Color().SetLinear(geometry.BiomeWeights.Get());
            auto normals = accessor.Normal();
            auto tangents = accessor.Tangent();
            auto uvs = accessor.TexCoord();
            for (int32 i = 0; i < count; i++)
            {
                normals.SetFloat3(i, geometry.Normals[i] * 0.5f + 0.5f);
                tangents.SetFloat4(i, Float4(geometry.Tangents[i] * 0.5f + 0.5f, 0.0f));
                uvs.SetFloat2(i, geometry.UVs[i]);
            }
            if (accessor.UpdateMesh(&model->LODs[0].Meshes[0]))
            {
                LOG(Error, "Failed to upload a quad sphere patch mesh.");
                model->DeleteObject();
                return false;
            }

            CollisionData* collision = nullptr;
            if (generateCollision)
            {
                collision = Content::CreateVirtualAsset<CollisionData>();
                if (!collision)
                {
                    LOG(Error, "Failed to create a quad sphere patch collision asset.");
                    model->DeleteObject();
                    return false;
                }
            }
            auto* renderer = New<StaticModel>();
            renderer->HideFlags = HideFlags::FullyHidden;
            renderer->SetIsActive(false);
            renderer->SetParent(&planet, false, false);
            renderer->SetLocalPosition(Vector3(geometry.Origin));
            renderer->Model = model;
            if (planet.Material.Get())
                renderer->SetMaterial(0, planet.Material.Get());
            if (!planet.CastShadows)
            {
                if (renderer->Entries.IsEmpty())
                    renderer->Entries.Resize(1);
                for (auto& entry : renderer->Entries)
                    entry.ShadowsMode = ShadowsCastingMode::None;
            }
            MeshCollider* collider = nullptr;
            if (collision)
            {
                collider = New<MeshCollider>();
                collider->HideFlags = HideFlags::FullyHidden;
                collider->SetIsActive(false);
                collider->SetParent(&planet, false, false);
                collider->SetLocalPosition(Vector3(geometry.Origin));
            }
            output.Key = geometry.Key;
            output.Origin = Double3(renderer->GetLocalPosition());
            if (retainSurfaceVertices)
                output.Vertices = geometry.Vertices;
            output.Renderer = renderer;
            output.Collider = collider;
            output.MeshModel = model;
            output.Collision = collision;
            output.CollisionAssigned = !generateCollision;
            return true;
        }

        static bool Destroy(QuadSpherePatchResources& patch)
        {
            if (!IsInMainThread())
            {
                LOG(Error, "Quad sphere patch cleanup must run on the main thread.");
                return false;
            }
            if (patch.Cooking)
            {
                patch.Cooking->Detached = true;
                auto expected = QuadSphereCookState::Queued;
                patch.Cooking->State.compare_exchange_strong(expected, QuadSphereCookState::Aborted, std::memory_order_acq_rel);
                patch.Cooking.reset();
            }
            if (auto* renderer = patch.Renderer.Get())
            {
                renderer->SetIsActive(false);
                renderer->DeleteObject();
            }
            patch.Renderer = nullptr;
            if (auto* collider = patch.Collider.Get())
            {
                collider->SetIsActive(false);
                collider->DeleteObject();
            }
            patch.Collider = nullptr;
            if (auto* model = patch.MeshModel.Get())
                model->DeleteObject();
            patch.MeshModel = nullptr;
            if (auto* collision = patch.Collision.Get())
                collision->DeleteObject();
            patch.Collision = nullptr;
            patch.CollisionAssigned = false;
            return true;
        }

        static bool CookCollision(CollisionData& collision, const QuadSpherePatchGeometry& geometry)
        {
#if COMPILE_WITH_PHYSICS_COOKING
            if (IsInMainThread())
            {
                LOG(Error, "Quad sphere collision cooking must run off the main thread.");
                return false;
            }
            if (collision.CookCollision(CollisionDataType::TriangleMesh, ToSpan(geometry.Vertices), ToSpan(geometry.Indices)))
            {
                LOG(Error, "Failed to cook a quad sphere patch collision mesh.");
                return false;
            }
            return true;
#else
            LOG(Error, "Quad sphere collision cooking is not available in this build.");
            return false;
#endif
        }

        static bool StartCollision(QuadSpherePatchResources& patch, QuadSpherePatchGeometry&& geometry)
        {
            if (!IsInMainThread())
            {
                LOG(Error, "Quad sphere collision jobs must be started on the main thread.");
                return false;
            }
#if !COMPILE_WITH_PHYSICS_COOKING || PLATFORM_THREADS_LIMIT <= 1
            LOG(Error, "Quad sphere collision cooking requires a build with physics cooking and worker threads.");
            return false;
#else
            auto* collision = patch.Collision.Get();
            if (!QuadSphereCollisionJobs.IsAcceptingJobs() || !collision || !patch.Collider.Get() || patch.Cooking ||
                patch.CollisionAssigned || !(geometry.Key == patch.Key) || geometry.Vertices.IsEmpty() ||
                geometry.Indices.IsEmpty() || geometry.Indices.Count() % 3 != 0)
            {
                LOG(Error, "Cannot start a quad sphere collision job with invalid patch resources.");
                return false;
            }
            for (const int32 index : geometry.Indices)
            {
                if (index < 0 || index >= geometry.Vertices.Count())
                {
                    LOG(Error, "Cannot cook a quad sphere patch with an invalid mesh index.");
                    return false;
                }
            }
            auto job = std::make_shared<QuadSphereCollisionJob>();
            job->Collision = collision;
            job->Geometry.Vertices = MoveTemp(geometry.Vertices);
            job->Geometry.Indices = MoveTemp(geometry.Indices);

            // A plain reference keeps the asset alive without scheduling an asset-loaded callback during cooking.
            collision->AddReference();
            patch.Collision = nullptr;
            patch.Cooking = job;
            QuadSphereCollisionJobs.Enqueue(job);
            return true;
#endif
        }

        static bool AssignCollision(QuadSpherePatchResources& patch)
        {
            if (!IsInMainThread())
            {
                LOG(Error, "Quad sphere collision assignment must run on the main thread.");
                return false;
            }
            if (patch.CollisionAssigned)
                return true;
            auto* collider = patch.Collider.Get();
            auto* collision = patch.Collision.Get();
            if (!collider || !collision || !collision->GetTriangle())
            {
                LOG(Error, "Cannot assign an unavailable or uncooked quad sphere collision mesh.");
                return false;
            }
            collider->CollisionData = collision;
            patch.CollisionAssigned = true;
            return true;
        }

        static QuadSphereCookState FinishCollision(QuadSpherePatchResources& patch)
        {
            if (!IsInMainThread() || !patch.Cooking)
            {
                LOG(Error, "Cannot finish an unavailable quad sphere collision job or finish it off the main thread.");
                return QuadSphereCookState::Failed;
            }
            auto job = patch.Cooking;
            const auto state = job->State.load(std::memory_order_acquire);
            if (state == QuadSphereCookState::Queued || state == QuadSphereCookState::Running)
                return state;
            if (state == QuadSphereCookState::Succeeded && job->Collision)
            {
                patch.Collision = job->Collision;
                job->Collision->RemoveReference();
                job->Collision = nullptr;
            }
            job->Detached = true;
            patch.Cooking.reset();
            if (state != QuadSphereCookState::Succeeded || !AssignCollision(patch))
            {
                LOG(Error, "Quad sphere collision generation did not produce an assignable mesh.");
                return QuadSphereCookState::Failed;
            }
            return QuadSphereCookState::Succeeded;
        }
    };

    void QuadSphereCollisionService::Enqueue(const std::shared_ptr<QuadSphereCollisionJob>& job)
    {
        _jobs.Add(job);
        Function<void()> work = [job]() mutable
        {
            auto current = MoveTemp(job);
            auto expected = QuadSphereCookState::Queued;
            if (!current->State.compare_exchange_strong(expected, QuadSphereCookState::Running, std::memory_order_acq_rel))
                return;
            const bool success = QuadSpherePatchInstallation::CookCollision(*current->Collision, current->Geometry);
            current->Geometry.Vertices = Array<Float3>();
            current->Geometry.Indices = Array<int32>();
            current->State.store(success ? QuadSphereCookState::Succeeded : QuadSphereCookState::Failed, std::memory_order_release);
        };
        Task::StartNew(work);
    }

    struct QuadSphereTerrainGeometry
    {
        static bool BrushLess(const QuadSphereBrushData& a, const QuadSphereBrushData& b)
        {
            if (a.Order != b.Order)
                return a.Order < b.Order;
            // Match System.Guid.CompareTo, including the two 16-bit fields in B.
            if (a.Id.A != b.Id.A)
                return a.Id.A < b.Id.A;
            const uint32 aB = a.Id.B & 0xffffu, bB = b.Id.B & 0xffffu;
            if (aB != bB)
                return aB < bB;
            const uint32 aC = a.Id.B >> 16, bC = b.Id.B >> 16;
            if (aC != bC)
                return aC < bC;
            for (int32 i = 8; i < 16; i++)
            {
                if (a.Id.Raw[i] != b.Id.Raw[i])
                    return a.Id.Raw[i] < b.Id.Raw[i];
            }
            return false;
        }

        static bool IsSpawnedPrefabBrush(const QuadSphereBrush* brush, const QuadSphereActor* planet, const Array<Guid>& spawnedPrefabRoots)
        {
            for (const Actor* actor = brush; actor && actor != planet; actor = actor->GetParent())
            {
                if (actor->Is<QuadSphereActor>())
                    break;
                if (spawnedPrefabRoots.Contains(actor->GetID()))
                    return true;
            }
            return false;
        }

        static bool CreateSnapshot(const QuadSphereActor& planet, bool collectBrushes,
                                   const Array<Guid>& spawnedPrefabRoots, QuadSphereTerrainSnapshot& output,
                                   HashSet<Guid>* invalidBrushes = nullptr)
        {
            if (!std::isfinite(planet.Radius) || planet.Radius <= 0.0 || planet.Radius > MAX_float / 4.0)
            {
                LOG(Error, "Quad sphere radius must be positive, finite, and representable by patch meshes.");
                return false;
            }
            if (!std::isfinite(planet.SkirtDepth) || !std::isfinite(planet.UvRebaseRepeat) || planet.UvRebaseRepeat < 0.0f ||
                (!planet.SimpleSphere && (!std::isfinite(planet.NoiseHeight) || !std::isfinite(planet.NoiseFrequency) ||
                    !std::isfinite(planet.BiomeBlendWidth) ||
                    !std::isfinite(planet.BeachWidth) || planet.BeachWidth < 0.0 || planet.BeachWidth > MAX_float / 4.0 ||
                    !std::isfinite(planet.BeachSmoothing) || (planet.BeachWidth > 0.0 &&
                    (!std::isfinite(planet.WaterHeight) || planet.WaterHeight <= 0.0 || planet.WaterHeight > MAX_float / 4.0)))))
            {
                LOG(Error, "Quad sphere settings must be finite, UV repeat and beach width must be nonnegative, and an enabled beach requires a positive water height.");
                return false;
            }
            const Transform& transform = planet.GetTransform();
            if (transform.Translation.IsNanOrInfinity() || transform.Orientation.IsNanOrInfinity() ||
                transform.Scale.IsNanOrInfinity() || transform.Scale.X == 0.0f || transform.Scale.Y == 0.0f || transform.Scale.Z == 0.0f)
            {
                LOG(Error, "Quad sphere terrain requires a finite transform with nonzero scale.");
                return false;
            }

            QuadSphereTerrainSnapshot snapshot;
            snapshot.Radius = planet.Radius;
            snapshot.SimpleSphere = planet.SimpleSphere;
            snapshot.RetainSurfaceVertices = !planet.FoliageAssets.IsEmpty();
            snapshot.Resolution = Math::Clamp(planet.PatchResolution, 2, 128);
            snapshot.ChunksPerPatch = Math::Clamp(planet.ChunksPerPatch, 1, 128);
            snapshot.SkirtDepth = Math::Max(0.0f, planet.SkirtDepth);
            snapshot.NoiseHeight = snapshot.SimpleSphere ? 0.0f : planet.NoiseHeight;
            snapshot.NoiseFrequency = snapshot.SimpleSphere ? 0.0f : Math::Max(0.0f, planet.NoiseFrequency);
            snapshot.NoiseOctaves = snapshot.SimpleSphere ? 0 : Math::Clamp(planet.NoiseOctaves, 0, 12);
            snapshot.NoiseSeed = planet.NoiseSeed;
            snapshot.UvRebaseRepeat = planet.UvRebaseRepeat;
            snapshot.BiomeLayerCount = Math::Clamp(planet.BiomeLayerCount, 1, 4);
            snapshot.BiomeLayers[0] = planet.BiomeLayer0;
            snapshot.BiomeLayers[1] = planet.BiomeLayer1;
            snapshot.BiomeLayers[2] = planet.BiomeLayer2;
            snapshot.BiomeLayers[3] = planet.BiomeLayer3;
            if (!snapshot.SimpleSphere)
            {
                for (int32 i = 0; i < snapshot.BiomeLayerCount; i++)
                {
                    auto& layer = snapshot.BiomeLayers[i];
                    if (!std::isfinite(layer.NoiseFrequency) || !std::isfinite(layer.NoiseContrast))
                    {
                        LOG(Error, "Quad sphere biome layer {0} noise settings must be finite.", i);
                        return false;
                    }
                    layer.NoiseFrequency = Math::Clamp(layer.NoiseFrequency, 0.0f, 4096.0f);
                    layer.NoiseOctaves = Math::Clamp(layer.NoiseOctaves, 1, 8);
                    layer.NoiseContrast = Math::Clamp(layer.NoiseContrast, 0.0f, 16.0f);
                }
                if (snapshot.NoiseOctaves > 0 && std::ldexp((double)snapshot.NoiseFrequency, snapshot.NoiseOctaves - 1) > (double)MAX_int32 - 1.0)
                {
                    LOG(Error, "Quad sphere noise frequency and octave count exceed the supported coordinate range.");
                    return false;
                }
            }
            snapshot.BiomeBlendWidth = snapshot.SimpleSphere ? 0.0f : Math::Clamp(planet.BiomeBlendWidth, 0.0f, 1.0f);
            snapshot.WaterHeight = snapshot.SimpleSphere ? snapshot.Radius : planet.WaterHeight;
            snapshot.BeachWidth = snapshot.SimpleSphere ? 0.0 : planet.BeachWidth;
            snapshot.BeachSmoothing = snapshot.SimpleSphere ? 0.0f : Math::Clamp(planet.BeachSmoothing, 0.0f, 1.0f);
            snapshot.PlanetTransform = transform;

            const double elevationBound = 2.0 * std::abs((double)snapshot.NoiseHeight) +
                snapshot.BeachWidth * snapshot.BeachSmoothing;
            if (!std::isfinite(elevationBound) || snapshot.Radius + elevationBound > MAX_float / 2.0)
            {
                LOG(Error, "Quad sphere elevation exceeds the representable patch mesh range.");
                return false;
            }
            snapshot.BaseElevationBound = elevationBound;
            if (collectBrushes && !CollectBrushes(planet, spawnedPrefabRoots, snapshot, invalidBrushes))
                return false;
            if (!ValidateBrushDisplacement(snapshot))
                return false;
            output = MoveTemp(snapshot);
            return true;
        }

        static bool CollectBrushes(const QuadSphereActor& planet, const Array<Guid>& spawnedPrefabRoots, QuadSphereTerrainSnapshot& snapshot,
                                   HashSet<Guid>* invalidBrushes = nullptr)
        {
            const Transform& transform = planet.GetTransform();
            if (transform.Translation.IsNanOrInfinity() || transform.Orientation.IsNanOrInfinity() ||
                transform.Scale.IsNanOrInfinity() || transform.Scale.X == 0.0f || transform.Scale.Y == 0.0f || transform.Scale.Z == 0.0f)
            {
                LOG(Error, "Quad sphere terrain requires a finite transform with nonzero scale.");
                return false;
            }
            snapshot.PlanetTransform = transform;
            snapshot.Brushes.Clear();
            HashSet<Guid> invalidThisScan;
            if (!snapshot.SimpleSphere)
            {
                const double scale = Math::Max(std::abs((double)transform.Scale.X), std::abs((double)transform.Scale.Y), std::abs((double)transform.Scale.Z));
                const double minScale = Math::Min(std::abs((double)transform.Scale.X), std::abs((double)transform.Scale.Y), std::abs((double)transform.Scale.Z));
                const double extent = Math::Max(0.01, snapshot.Radius + snapshot.BaseElevationBound) * scale;
                const double innerExtent = Math::Max(0.01, snapshot.Radius - snapshot.BaseElevationBound) * minScale;
                Array<Actor*> actors;
                SceneQuery::GetAllActors(actors);
                for (Actor* actor : actors)
                {
                    auto* brush = ScriptingObject::Cast<QuadSphereBrush>(actor);
                    if (!brush || !brush->IsActiveInHierarchy() ||
                        (!brush->TargetsActor(&planet) && !IsSpawnedPrefabBrush(brush, &planet, spawnedPrefabRoots)))
                        continue;
                    const Guid id = brush->GetID();
                    const bool warn = !invalidBrushes || !invalidBrushes->Contains(id);
                    if (!std::isfinite(brush->Radius) || brush->Radius <= 0.0f || !std::isfinite(brush->Falloff) || !std::isfinite(brush->Depth) ||
                        (brush->Mode != QuadSphereBrushMode::Carve && brush->Mode != QuadSphereBrushMode::Flatten &&
                         brush->Mode != QuadSphereBrushMode::None && brush->Mode != QuadSphereBrushMode::RemoveFoliage) ||
                        (brush->Shape != QuadSphereBrushShape::Circle && brush->Shape != QuadSphereBrushShape::Square))
                    {
                        if (warn)
                            LOG(Warning, "Quad sphere brush '{0}' has invalid settings and will be ignored.", brush->GetName());
                        invalidThisScan.Add(id);
                        continue;
                    }
                    const Vector3 position = brush->GetPosition();
                    const double distance = (position - transform.Translation).Length();
                    if (position.IsNanOrInfinity() || !std::isfinite(distance))
                    {
                        if (warn)
                            LOG(Warning, "Quad sphere brush '{0}' has an invalid position and will be ignored.", brush->GetName());
                        invalidThisScan.Add(id);
                        continue;
                    }
                    const double brushExtent = brush->Radius * (brush->Shape == QuadSphereBrushShape::Square ? std::sqrt(3.0) : 1.0);
                    if ((std::isfinite(extent) && distance > extent + brushExtent) ||
                        (std::isfinite(innerExtent) && distance + brushExtent < innerExtent))
                        continue;
                    const double flattenHeight = brush->Mode == QuadSphereBrushMode::Flatten ?
                        (double)transform.WorldToLocal(position).Length() - snapshot.Radius : 0.0;
                    if (!std::isfinite(flattenHeight))
                    {
                        if (warn)
                            LOG(Warning, "Quad sphere brush '{0}' has an invalid flatten height and will be ignored.", brush->GetName());
                        invalidThisScan.Add(id);
                        continue;
                    }
                    QuadSphereBrushData data;
                    data.Id = id;
                    data.Order = brush->Order;
                    data.Mode = brush->Mode;
                    data.Shape = brush->Shape;
                    data.BiomeOverride = brush->BiomeOverride;
                    const bool invalidBiome = (int32)data.BiomeOverride < (int32)QuadSphereBrushBiome::None ||
                        (int32)data.BiomeOverride > (int32)QuadSphereBrushBiome::Beach;
                    if (invalidBiome)
                    {
                        data.BiomeOverride = QuadSphereBrushBiome::None;
                    }
                    data.Position = position;
                    data.Radius = brush->Radius;
                    data.Falloff = Math::Clamp(brush->Falloff, 0.0f, 1.0f);
                    data.Depth = Math::Max(0.0f, brush->Depth);
                    data.FlattenHeight = flattenHeight;
                    if (data.Shape == QuadSphereBrushShape::Square)
                    {
                        QuadSphereBrush::GetSquareAxes(position, transform.Translation, brush->GetOrientation(), data.AxisNormal, data.AxisX, data.AxisY);
                        if (data.AxisX.IsNanOrInfinity() || data.AxisY.IsNanOrInfinity())
                        {
                            if (warn)
                                LOG(Warning, "Quad sphere brush '{0}' has invalid square axes and will be ignored.", brush->GetName());
                            invalidThisScan.Add(id);
                            continue;
                        }
                    }
                    if (invalidBiome)
                    {
                        if (warn)
                            LOG(Warning, "Quad sphere brush '{0}' has an invalid biome override; the override will be ignored.", brush->GetName());
                        invalidThisScan.Add(id);
                    }
                    snapshot.Brushes.Add(data);
                }
                Sorting::QuickSort(snapshot.Brushes.Get(), snapshot.Brushes.Count(), &BrushLess);
            }
            if (invalidBrushes)
                *invalidBrushes = MoveTemp(invalidThisScan);
            return true;
        }

        static double BrushDisplacement(const QuadSphereTerrainSnapshot& snapshot)
        {
            double displacement = snapshot.BaseElevationBound;
            for (const auto& brush : snapshot.Brushes)
            {
                if (brush.Mode == QuadSphereBrushMode::Carve)
                    displacement += brush.Depth;
                else if (brush.Mode == QuadSphereBrushMode::Flatten)
                    displacement = Math::Max(displacement, std::abs(brush.FlattenHeight));
            }
            return displacement;
        }

        static bool ValidateBrushDisplacement(const QuadSphereTerrainSnapshot& snapshot)
        {
            const double displacement = BrushDisplacement(snapshot);
            if (!std::isfinite(displacement) || 2.0 * snapshot.Radius + displacement + snapshot.SkirtDepth > MAX_float)
            {
                LOG(Error, "Quad sphere brush displacement or skirt depth exceeds the representable patch mesh range.");
                return false;
            }
            return true;
        }

        static Double3 Direction(int32 face, double u, double v)
        {
            Double3 cube;
            switch (face)
            {
            case 0: cube = Double3(1, v, -u); break;
            case 1: cube = Double3(-1, v, u); break;
            case 2: cube = Double3(u, 1, -v); break;
            case 3: cube = Double3(u, -1, v); break;
            case 4: cube = Double3(u, v, 1); break;
            default: cube = Double3(-u, v, -1); break;
            }
            return Double3::Normalize(cube);
        }

        static double ValueNoise(const Double3& p, uint32 seed)
        {
            const int32 x = (int32)std::floor(p.X);
            const int32 y = (int32)std::floor(p.Y);
            const int32 z = (int32)std::floor(p.Z);
            double tx = p.X - x, ty = p.Y - y, tz = p.Z - z;
            tx = tx * tx * (3.0 - 2.0 * tx);
            ty = ty * ty * (3.0 - 2.0 * ty);
            tz = tz * tz * (3.0 - 2.0 * tz);
            double result = 0.0;
            for (int32 dz = 0; dz <= 1; dz++)
                for (int32 dy = 0; dy <= 1; dy++)
                    for (int32 dx = 0; dx <= 1; dx++)
                    {
                        uint32 hash = ((uint32)x + dx) * 73856093u ^ ((uint32)y + dy) * 19349663u ^
                                      ((uint32)z + dz) * 83492791u ^ seed;
                        hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15;
                        hash *= 0x846ca68bu; hash ^= hash >> 16;
                        const double value = hash / (double)MAX_uint32 * 2.0 - 1.0;
                        result += value * (dx == 0 ? 1.0 - tx : tx) * (dy == 0 ? 1.0 - ty : ty) * (dz == 0 ? 1.0 - tz : tz);
                    }
            return result;
        }

        static double BaseElevation(const Double3& direction, const QuadSphereTerrainSnapshot& settings)
        {
            if (settings.SimpleSphere)
                return 0.0;
            double height = 0.0;
            double frequency = settings.NoiseFrequency, amplitude = settings.NoiseHeight;
            for (int32 i = 0; i < settings.NoiseOctaves; i++)
            {
                height += ValueNoise(direction * frequency, (uint32)settings.NoiseSeed + (uint32)i) * amplitude;
                frequency *= 2.0;
                amplitude *= 0.5;
            }
            return height;
        }

        static double BeachWeight(double height, const QuadSphereTerrainSnapshot& settings)
        {
            if (settings.SimpleSphere || settings.BeachWidth <= 0.0)
                return 0.0;
            const double distance = std::abs(height - (settings.WaterHeight - settings.Radius));
            const double weight = Math::Clamp(1.0 - distance / settings.BeachWidth, 0.0, 1.0);
            return weight * weight * (3.0 - 2.0 * weight);
        }

        static double FlattenBeach(double height, double weight, const QuadSphereTerrainSnapshot& settings)
        {
            if (weight <= 0.0 || settings.BeachSmoothing <= 0.0f)
                return height;
            return height + ((settings.WaterHeight - settings.Radius) - height) * (weight * settings.BeachSmoothing);
        }

        static double BrushWeight(const Vector3& worldPosition, const QuadSphereBrushData& brush)
        {
            const Vector3 offset = worldPosition - brush.Position;
            const double distance = brush.Shape == QuadSphereBrushShape::Square
                ? Math::Max(std::abs((double)Vector3::Dot(offset, brush.AxisNormal)),
                            Math::Max(std::abs((double)Vector3::Dot(offset, brush.AxisX)), std::abs((double)Vector3::Dot(offset, brush.AxisY))))
                : (double)offset.Length();
            if (distance >= brush.Radius)
                return 0.0;
            const double weight = brush.Falloff <= 0.0f ? 1.0 : Math::Clamp((brush.Radius - distance) / (brush.Radius * brush.Falloff), 0.0, 1.0);
            return weight * weight * (3.0 - 2.0 * weight);
        }

        static double BrushElevation(const Double3& direction, double height, const QuadSphereTerrainSnapshot& settings)
        {
            if (settings.SimpleSphere || settings.Brushes.IsEmpty())
                return height;
            const Vector3 worldPosition = settings.PlanetTransform.LocalToWorld(Vector3(direction * Math::Max(0.01, settings.Radius + height)));
            for (const auto& brush : settings.Brushes)
            {
                const double weight = BrushWeight(worldPosition, brush);
                if (brush.Mode == QuadSphereBrushMode::Carve)
                    height -= brush.Depth * weight;
                else if (brush.Mode == QuadSphereBrushMode::Flatten)
                    height += (brush.FlattenHeight - height) * weight;
            }
            return height;
        }

        static Double3 Surface(const Double3& direction, const QuadSphereTerrainSnapshot& settings)
        {
            double height = BaseElevation(direction, settings);
            height = FlattenBeach(height, BeachWeight(height, settings), settings);
            height = BrushElevation(direction, height, settings);
            return direction * Math::Max(0.01, settings.Radius + height);
        }

        static Double3 SurfaceNormal(const Double3& direction, const QuadSphereTerrainSnapshot& settings)
        {
            if (settings.SimpleSphere)
                return direction;
            const Double3 reference = std::abs(direction.Y) < 0.9 ? Double3::UnitY : Double3::UnitX;
            const Double3 right = Double3::Normalize(Double3::Cross(reference, direction));
            const Double3 up = Double3::Cross(direction, right);
            constexpr double epsilon = 0.0001;
            const Double3 dx = Surface(Double3::Normalize(direction + right * epsilon), settings) - Surface(Double3::Normalize(direction - right * epsilon), settings);
            const Double3 dy = Surface(Double3::Normalize(direction + up * epsilon), settings) - Surface(Double3::Normalize(direction - up * epsilon), settings);
            const Double3 normal = Double3::Cross(dx, dy);
            return normal.LengthSquared() > 0.000001 ? Double3::Normalize(normal) : direction;
        }

        static Double3 SurfaceTangent(const Double3& direction, const Double3& normal, const Double3& uAxis)
        {
            Double3 tangent = uAxis * Double3::Dot(normal, direction) - direction * Double3::Dot(normal, uAxis);
            if (tangent.LengthSquared() < 0.000000000001)
            {
                tangent = uAxis - normal * Double3::Dot(normal, uAxis);
                if (tangent.LengthSquared() < 0.000000000001)
                    tangent = Double3::Cross(normal, std::abs(normal.Y) < 0.9 ? Double3::UnitY : Double3::UnitX);
            }
            return Double3::Normalize(tangent);
        }

        static void SelectPatches(Array<QuadSpherePatchKey>& result, const QuadSpherePatchKey& key,
                                  const Vector3& target, bool hasTarget, int32 maxLod, float lodDistance,
                                  const QuadSphereTerrainSnapshot& settings)
        {
            if (hasTarget && key.Level < Math::Clamp(maxLod, 0, 10))
            {
                const int32 divisions = settings.ChunksPerPatch << key.Level;
                const double u = -1.0 + 2.0 * (key.X + 0.5) / divisions;
                const double v = -1.0 + 2.0 * (key.Y + 0.5) / divisions;
                const Vector3 center(Surface(Direction(key.Face, u, v), settings));
                const double distance = (target - center).Length();
                const double patchSize = settings.Radius * 2.0 / divisions;
                if (distance < patchSize * Math::Max(0.1f, lodDistance))
                {
                    for (int32 dy = 0; dy < 2; dy++)
                        for (int32 dx = 0; dx < 2; dx++)
                            SelectPatches(result, { key.Face, key.Level + 1, key.X * 2 + dx, key.Y * 2 + dy },
                                          target, true, maxLod, lodDistance, settings);
                    return;
                }
            }
            result.Add(key);
        }

        static Color32 BiomeWeights(const Double3& direction, const QuadSphereTerrainSnapshot& settings)
        {
            const int32 count = Math::Clamp(settings.BiomeLayerCount, 1, 4);
            if (settings.SimpleSphere || count == 1)
                return Color32(255, 0, 0, 0);
            double scores[4] = {};
            double maximum = -MAX_double;
            int32 winner = 0;
            for (int32 layerIndex = 0; layerIndex < count; layerIndex++)
            {
                const auto& layer = settings.BiomeLayers[layerIndex];
                double noise = 0.0, amplitude = 1.0, amplitudeSum = 0.0, frequency = layer.NoiseFrequency;
                for (int32 octave = 0; octave < layer.NoiseOctaves; octave++)
                {
                    const uint32 seed = (uint32)settings.NoiseSeed + (uint32)layer.SeedOffset + (uint32)octave * 1013u;
                    noise += ValueNoise(direction * frequency, seed) * amplitude;
                    amplitudeSum += amplitude;
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }
                scores[layerIndex] = amplitudeSum > 0.0 ? noise / amplitudeSum * layer.NoiseContrast : 0.0;
                if (scores[layerIndex] > maximum)
                {
                    maximum = scores[layerIndex];
                    winner = layerIndex;
                }
            }
            byte weights[4] = {};
            if (settings.BiomeBlendWidth <= 0.0f)
            {
                weights[winner] = 255;
                return Color32(weights[0], weights[1], weights[2], weights[3]);
            }
            double total = 0.0;
            for (int32 i = 0; i < count; i++)
            {
                const double blend = Math::Clamp(1.0 - (maximum - scores[i]) / settings.BiomeBlendWidth, 0.0, 1.0);
                scores[i] = blend * blend * (3.0 - 2.0 * blend);
                total += scores[i];
            }
            int32 remaining = 255;
            for (int32 i = 0; i < count; i++)
            {
                const double scaled = scores[i] * 255.0 / total;
                weights[i] = (byte)scaled;
                remaining -= weights[i];
                scores[i] = scaled - weights[i];
            }
            while (remaining-- > 0)
            {
                int32 best = 0;
                for (int32 i = 1; i < count; i++)
                    if (scores[i] > scores[best])
                        best = i;
                weights[best]++;
                scores[best] = -1.0;
            }
            return Color32(weights[0], weights[1], weights[2], weights[3]);
        }

        static Color32 ApplyBeachWeight(const Color32& biome, double beach)
        {
            if (beach <= 0.0)
                return biome;
            if (beach >= 1.0)
                return Color32(0, 0, 0, 0);
            const double scaledBeach = Math::Clamp(beach, 0.0, 1.0) * 255.0;
            const double floorBeach = std::floor(scaledBeach);
            const double fraction = scaledBeach - floorBeach;
            const int32 roundedBeach = (int32)floorBeach + (fraction > 0.5 || (fraction == 0.5 && ((int32)floorBeach & 1)) ? 1 : 0);
            const int32 biomeTotal = 255 - roundedBeach;
            const byte components[4] = { biome.R, biome.G, biome.B, biome.A };
            byte weights[4];
            int32 remainders[4], remaining = biomeTotal;
            for (int32 i = 0; i < 4; i++)
            {
                const int32 scaled = components[i] * biomeTotal;
                weights[i] = (byte)(scaled / 255);
                remainders[i] = scaled % 255;
                remaining -= weights[i];
            }
            while (remaining-- > 0)
            {
                int32 best = 0;
                for (int32 i = 1; i < 4; i++)
                    if (remainders[i] > remainders[best])
                        best = i;
                weights[best]++;
                remainders[best] = -1;
            }
            return Color32(weights[0], weights[1], weights[2], weights[3]);
        }

        static Color32 ApplyBrushBiomes(const Double3& direction, double baseHeight, const Color32& biome,
                                        const QuadSphereTerrainSnapshot& settings, bool* wasApplied = nullptr)
        {
            if (wasApplied)
                *wasApplied = false;
            if (settings.SimpleSphere || settings.Brushes.IsEmpty())
                return biome;
            const Vector3 worldPosition = settings.PlanetTransform.LocalToWorld(Vector3(direction * Math::Max(0.01, settings.Radius + baseHeight)));
            double weights[5] = { (double)biome.R, (double)biome.G, (double)biome.B, (double)biome.A,
                                  (double)(255 - biome.R - biome.G - biome.B - biome.A) };
            bool applied = false;
            for (const auto& brush : settings.Brushes)
            {
                const int32 target = (int32)brush.BiomeOverride - 1;
                if (target < 0 || target > 4)
                    continue;
                const double weight = BrushWeight(worldPosition, brush);
                if (weight <= 0.0)
                    continue;
                applied = true;
                for (int32 i = 0; i < 5; i++)
                    weights[i] = weights[i] * (1.0 - weight) + (i == target ? 255.0 * weight : 0.0);
            }
            if (!applied)
                return biome;
            if (wasApplied)
                *wasApplied = true;
            byte quantized[5];
            int32 remaining = 255;
            for (int32 i = 0; i < 5; i++)
            {
                quantized[i] = (byte)Math::Clamp((int32)std::floor(weights[i]), 0, 255);
                remaining -= quantized[i];
                weights[i] -= quantized[i];
            }
            while (remaining-- > 0)
            {
                int32 best = 0;
                for (int32 i = 1; i < 5; i++)
                    if (weights[i] > weights[best])
                        best = i;
                quantized[best]++;
                weights[best] = -1.0;
            }
            return Color32(quantized[0], quantized[1], quantized[2], quantized[3]);
        }

        static double PlacementRandom(int32 seed, int32 root, int32 entry, int32 attempt, int32 channel)
        {
            uint32 hash = (uint32)seed ^ (uint32)root * 0x9e3779b9u ^ (uint32)entry * 0x85ebca6bu ^
                (uint32)attempt * 0xc2b2ae35u ^ (uint32)channel * 0x27d4eb2du;
            hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15;
            hash *= 0x846ca68bu; hash ^= hash >> 16;
            return hash / 4294967296.0;
        }

        static double GradientNoise(const Double3& p, uint32 seed)
        {
            const int32 x = (int32)std::floor(p.X), y = (int32)std::floor(p.Y), z = (int32)std::floor(p.Z);
            const double fx = p.X - x, fy = p.Y - y, fz = p.Z - z;
            const double tx = fx * fx * fx * (fx * (fx * 6.0 - 15.0) + 10.0);
            const double ty = fy * fy * fy * (fy * (fy * 6.0 - 15.0) + 10.0);
            const double tz = fz * fz * fz * (fz * (fz * 6.0 - 15.0) + 10.0);
            double result = 0.0;
            for (int32 dz = 0; dz <= 1; dz++)
                for (int32 dy = 0; dy <= 1; dy++)
                    for (int32 dx = 0; dx <= 1; dx++)
                    {
                        uint32 hash = ((uint32)x + dx) * 73856093u ^ ((uint32)y + dy) * 19349663u ^
                            ((uint32)z + dz) * 83492791u ^ seed;
                        hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15;
                        hash *= 0x846ca68bu; hash ^= hash >> 16;
                        const int32 gradient = (int32)(hash % 12);
                        const double gx = fx - dx, gy = fy - dy, gz = fz - dz;
                        const double a = gradient < 8 ? gx : gy;
                        const double b = gradient < 4 ? gy : gz;
                        const double value = ((gradient & 1) == 0 ? a : -a) + ((gradient & 2) == 0 ? b : -b);
                        result += value * (dx == 0 ? 1.0 - tx : tx) * (dy == 0 ? 1.0 - ty : ty) * (dz == 0 ? 1.0 - tz : tz);
                    }
            return result;
        }

        static double PlacementNoise(const Double3& direction, float noiseFrequency, int32 noiseOctaves, int32 seedOffset,
                                     QuadSphereFoliageNoiseType noiseType, int32 seed)
        {
            double noise = 0.0, amplitude = 1.0, total = 0.0;
            double frequency = Math::Clamp((double)noiseFrequency, 0.0, 4096.0);
            for (int32 octave = 0; octave < Math::Clamp(noiseOctaves, 1, 8); octave++)
            {
                const uint32 octaveSeed = (uint32)seed + (uint32)seedOffset + (uint32)octave * 1013u;
                const Double3 p = direction * frequency;
                double sample = noiseType == QuadSphereFoliageNoiseType::Value ? ValueNoise(p, octaveSeed) : GradientNoise(p, octaveSeed);
                if (noiseType == QuadSphereFoliageNoiseType::RidgedPerlin)
                    sample = 2.0 * (1.0 - Math::Clamp(std::abs(sample), 0.0, 1.0)) - 1.0;
                noise += sample * amplitude;
                total += amplitude;
                amplitude *= 0.5;
                frequency *= 2.0;
            }
            return Math::Clamp((noise / total + 1.0) * 0.5, 0.0, 1.0);
        }

        static bool ValidPrefabConfig(const QuadSpherePrefabConfig& config)
        {
            return std::isfinite(config.SpawnDistance) && config.SpawnDistance >= 0.0 &&
                std::isfinite(config.Density) && config.Density > 0.0f &&
                std::isfinite(config.Probability) && config.Probability > 0.0f &&
                std::isfinite(config.NoiseFrequency) && config.NoiseFrequency >= 0.0f && std::isfinite(config.NoiseStrength) &&
                std::isfinite(config.MinAltitude) && std::isfinite(config.MaxAltitude) &&
                (config.MinAltitude == 0.0 || config.MaxAltitude == 0.0 || config.MaxAltitude >= config.MinAltitude) &&
                std::isfinite(config.AltitudeFalloff) && config.AltitudeFalloff >= 0.0f &&
                std::isfinite(config.MinSlope) && std::isfinite(config.MaxSlope) && config.MaxSlope >= config.MinSlope &&
                std::isfinite(config.MinTemperature) && std::isfinite(config.MaxTemperature) &&
                (config.Hemisphere == QuadSphereFoliageHemisphere::Both || config.Hemisphere == QuadSphereFoliageHemisphere::North ||
                 config.Hemisphere == QuadSphereFoliageHemisphere::South);
        }

        static bool ValidFoliageConfig(const QuadSphereFoliageConfig& config)
        {
            return std::isfinite(config.Density) && config.Density > 0.0f &&
                std::isfinite(config.Probability) && config.Probability > 0.0f &&
                std::isfinite(config.NoiseFrequency) && config.NoiseFrequency >= 0.0f &&
                std::isfinite(config.NoiseDensityStrength) && std::isfinite(config.NoiseScaleStrength) &&
                (config.NoiseType == QuadSphereFoliageNoiseType::Value || config.NoiseType == QuadSphereFoliageNoiseType::Perlin ||
                 config.NoiseType == QuadSphereFoliageNoiseType::RidgedPerlin) &&
                std::isfinite(config.MinAltitude) && std::isfinite(config.MaxAltitude) &&
                (config.MinAltitude == 0.0 || config.MaxAltitude == 0.0 || config.MaxAltitude >= config.MinAltitude) &&
                std::isfinite(config.MinSlope) && std::isfinite(config.MaxSlope) && config.MaxSlope >= config.MinSlope &&
                std::isfinite(config.MinTemperature) && std::isfinite(config.MaxTemperature) &&
                std::isfinite(config.MinScale) && std::isfinite(config.MaxScale) && config.MinScale > 0.0f && config.MaxScale >= config.MinScale &&
                (config.Hemisphere == QuadSphereFoliageHemisphere::Both || config.Hemisphere == QuadSphereFoliageHemisphere::North ||
                 config.Hemisphere == QuadSphereFoliageHemisphere::South);
        }

        static bool TryPrefabPlacement(const QuadSphereTerrainSnapshot& settings, const QuadSpherePrefabConfig& config,
                                       int32 root, int32 entry, int32 face, int32 x, int32 y, int32 attempt, Vector3& surface)
        {
            constexpr double pi = 3.14159265358979323846;
            const int32 seed = settings.NoiseSeed;
            const int32 divisions = settings.ChunksPerPatch;
            const double u = -1.0 + 2.0 * (x + PlacementRandom(seed, root, entry, attempt, 1)) / divisions;
            const double v = -1.0 + 2.0 * (y + PlacementRandom(seed, root, entry, attempt, 2)) / divisions;
            const double areaWeight = std::pow(1.0 + u * u + v * v, -1.5);
            if (PlacementRandom(seed, root, entry, attempt, 3) >= areaWeight)
                return false;
            const Double3 direction = Direction(face, u, v);
            if ((config.Hemisphere == QuadSphereFoliageHemisphere::North && direction.Y < 0.0) ||
                (config.Hemisphere == QuadSphereFoliageHemisphere::South && direction.Y > 0.0))
                return false;
            if (config.MinTemperature > 0.0f || config.MaxTemperature < 1.0f)
            {
                const double temperature = 1.0 - std::asin(Math::Clamp(std::abs(direction.Y), 0.0, 1.0)) / (pi * 0.5);
                if (temperature < config.MinTemperature || temperature > config.MaxTemperature)
                    return false;
            }
            const Double3 point = Surface(direction, settings);
            const double altitude = point.Length();
            const bool hasMin = config.MinAltitude != 0.0, hasMax = config.MaxAltitude != 0.0;
            if ((hasMin && altitude < config.MinAltitude) || (hasMax && altitude > config.MaxAltitude))
                return false;
            double altitudeWeight = 1.0;
            if (config.AltitudeFalloff > 0.0f && (hasMin || hasMax))
            {
                double distance = MAX_double;
                if (hasMin)
                    distance = Math::Min(distance, altitude - config.MinAltitude);
                if (hasMax)
                    distance = Math::Min(distance, config.MaxAltitude - altitude);
                altitudeWeight = Math::Clamp(distance / config.AltitudeFalloff, 0.0, 1.0);
                if (altitudeWeight <= 0.0)
                    return false;
            }
            const Double3 normal = SurfaceNormal(direction, settings);
            const double slope = settings.SimpleSphere ? 0.0 : std::acos(Math::Clamp(Double3::Dot(normal, direction), -1.0, 1.0)) * (180.0 / pi);
            if (!std::isfinite(slope) || slope < config.MinSlope || slope > config.MaxSlope)
                return false;

            const double height = BaseElevation(direction, settings);
            const double beach = BeachWeight(height, settings);
            const Color32 biome = BiomeWeights(direction, settings);
            const byte weights[4] = { biome.R, biome.G, biome.B, biome.A };
            double biomeWeight = 0.0;
            for (int32 i = 0; i < 4; i++)
                if (((int32)config.Biomes & (1 << i)) != 0)
                    biomeWeight += weights[i] / 255.0 * (1.0 - beach);
            if (((int32)config.Biomes & (int32)QuadSpherePrefabBiomes::Beach) != 0)
                biomeWeight += beach;
            if (biomeWeight <= 0.0)
                return false;

            double noiseWeight = 1.0;
            if (config.NoiseStrength > 0.0f)
            {
                const double placementNoise = PlacementNoise(direction, config.NoiseFrequency, config.NoiseOctaves,
                    config.SeedOffset, QuadSphereFoliageNoiseType::Value, seed);
                noiseWeight -= Math::Clamp((double)config.NoiseStrength, 0.0, 1.0) * (1.0 - placementNoise);
            }
            const double probability = Math::Clamp((double)config.Probability, 0.0, 1.0) * biomeWeight * altitudeWeight * noiseWeight;
            if (PlacementRandom(seed, root, entry, attempt, 4) >= probability)
                return false;
            surface = Vector3(point);
            return !surface.IsNanOrInfinity();
        }

        static bool TrySamplePatchSurface(const QuadSpherePatchResources& patch, int32 resolution, double u, double v,
                                          const Double3& direction, Double3& surface)
        {
            if (resolution < 2 || patch.Vertices.Count() < (resolution + 1) * (resolution + 1))
                return false;
            const double gridX = Math::Clamp(u * resolution, 0.0, (double)resolution);
            const double gridY = Math::Clamp(v * resolution, 0.0, (double)resolution);
            const int32 x = Math::Min((int32)gridX, resolution - 1);
            const int32 y = Math::Min((int32)gridY, resolution - 1);
            int32 a = y * (resolution + 1) + x;
            const int32 b = a + 1, c = a + resolution + 1, d = c + 1;
            if (gridX - x + gridY - y > 1.0)
                a = d;
            const Double3 p0 = patch.Origin + Double3(patch.Vertices[a]);
            const Double3 p1 = patch.Origin + Double3(patch.Vertices[b]);
            const Double3 p2 = patch.Origin + Double3(patch.Vertices[c]);
            const Double3 planeNormal = Double3::Cross(p1 - p0, p2 - p0);
            const double denominator = Double3::Dot(planeNormal, direction);
            if (!std::isfinite(denominator) || std::abs(denominator) < 1e-20)
                return false;
            const double distance = Double3::Dot(planeNormal, p0) / denominator;
            if (!std::isfinite(distance) || distance <= 0.0)
                return false;
            surface = direction * distance;
            return !surface.IsNanOrInfinity();
        }

        static bool BuildPatch(const QuadSpherePatchKey& key, const QuadSphereTerrainSnapshot& settings,
                               const std::atomic<bool>& cancelled, QuadSpherePatchGeometry& output)
        {
            const int32 n = settings.Resolution;
            const int32 patchesPerChunk = 1 << key.Level;
            const int32 divisions = settings.ChunksPerPatch * patchesPerChunk;
            const double step = 2.0 / divisions;
            const double u0 = -1.0 + key.X * step, v0 = -1.0 + key.Y * step;
            const double uvStartX = (key.X % patchesPerChunk) * n / (double)patchesPerChunk;
            const double uvStartY = (key.Y % patchesPerChunk) * n / (double)patchesPerChunk;
            const double uvOffsetX = settings.UvRebaseRepeat > 0 ? std::floor(uvStartX * settings.UvRebaseRepeat) / settings.UvRebaseRepeat : 0.0;
            const double uvOffsetY = settings.UvRebaseRepeat > 0 ? std::floor(uvStartY * settings.UvRebaseRepeat) / settings.UvRebaseRepeat : 0.0;
            QuadSpherePatchGeometry geometry;
            geometry.Key = key;
            geometry.Origin = Direction(key.Face, u0 + step * 0.5, v0 + step * 0.5) * settings.Radius;
            const Double3 uAxis = key.Face == 0 ? Double3(0, 0, -1) : key.Face == 1 ? Double3::UnitZ : key.Face == 5 ? Double3(-1, 0, 0) : Double3::UnitX;
            const int32 vertexCapacity = (n + 1) * (n + 1) + 4 * (n + 1);
            geometry.Vertices.EnsureCapacity(vertexCapacity);
            geometry.Normals.EnsureCapacity(vertexCapacity);
            geometry.Tangents.EnsureCapacity(vertexCapacity);
            geometry.UVs.EnsureCapacity(vertexCapacity);
            geometry.BiomeWeights.EnsureCapacity(vertexCapacity);
            geometry.Indices.EnsureCapacity(n * n * 6 + n * 24);

            for (int32 y = 0; y <= n; y++)
            {
                if (cancelled.load())
                    return false;
                for (int32 x = 0; x <= n; x++)
                {
                    const double u = -1.0 + 2.0 * ((double)key.X * n + x) / ((double)divisions * n);
                    const double v = -1.0 + 2.0 * ((double)key.Y * n + y) / ((double)divisions * n);
                    const Double3 direction = Direction(key.Face, u, v);
                    const Double3 normal = SurfaceNormal(direction, settings);
                    double height = BaseElevation(direction, settings);
                    const double beach = BeachWeight(height, settings);
                    height = FlattenBeach(height, beach, settings);
                    const Color32 biome = ApplyBrushBiomes(direction, height, ApplyBeachWeight(BiomeWeights(direction, settings), beach), settings);
                    height = BrushElevation(direction, height, settings);
                    geometry.Vertices.Add(Float3(direction * Math::Max(0.01, settings.Radius + height) - geometry.Origin));
                    geometry.Normals.Add(Float3(normal));
                    geometry.Tangents.Add(Float3(SurfaceTangent(direction, normal, uAxis)));
                    geometry.BiomeWeights.Add(biome);
                    geometry.UVs.Add(Float2((float)(uvStartX + x / (double)patchesPerChunk - uvOffsetX),
                                            (float)(uvStartY + y / (double)patchesPerChunk - uvOffsetY)));
                }
            }
            for (int32 y = 0; y < n; y++)
                for (int32 x = 0; x < n; x++)
                {
                    const int32 a = y * (n + 1) + x, b = a + 1, c = a + n + 1;
                    geometry.Indices.Add(a); geometry.Indices.Add(b); geometry.Indices.Add(c);
                    geometry.Indices.Add(b); geometry.Indices.Add(c + 1); geometry.Indices.Add(c);
                }
            for (int32 edge = 0; edge < 4 && settings.SkirtDepth > 0.0f; edge++)
            {
                const int32 start = geometry.Vertices.Count();
                for (int32 i = 0; i <= n; i++)
                {
                    const int32 source = edge == 0 ? i : edge == 1 ? i * (n + 1) + n : edge == 2 ? n * (n + 1) + n - i : (n - i) * (n + 1);
                    const int32 sourceX = source % (n + 1), sourceY = source / (n + 1);
                    const double u = -1.0 + 2.0 * ((double)key.X * n + sourceX) / ((double)divisions * n);
                    const double v = -1.0 + 2.0 * ((double)key.Y * n + sourceY) / ((double)divisions * n);
                    geometry.Vertices.Add(geometry.Vertices[source] - Float3(Direction(key.Face, u, v) * (double)settings.SkirtDepth));
                    geometry.Normals.Add(geometry.Normals[source]);
                    geometry.Tangents.Add(geometry.Tangents[source]);
                    geometry.UVs.Add(geometry.UVs[source]);
                    geometry.BiomeWeights.Add(geometry.BiomeWeights[source]);
                    if (i == 0)
                        continue;
                    const int32 previous = edge == 0 ? i - 1 : edge == 1 ? (i - 1) * (n + 1) + n : edge == 2 ? n * (n + 1) + n - i + 1 : (n - i + 1) * (n + 1);
                    geometry.Indices.Add(previous); geometry.Indices.Add(start + i - 1); geometry.Indices.Add(source);
                    geometry.Indices.Add(source); geometry.Indices.Add(start + i - 1); geometry.Indices.Add(start + i);
                }
            }
            output = MoveTemp(geometry);
            return true;
        }
    };

    enum class QuadSphereGenerationResult
    {
        Pending,
        Ready,
        Complete,
        Failed,
    };

    // Jobs own their immutable snapshot and cancellation token, not the actor.
    class QuadSphereGeometryBatch
    {
        struct Job
        {
            QuadSpherePatchKey Key;
            std::shared_ptr<const QuadSphereTerrainSnapshot> Snapshot;
            std::shared_ptr<std::atomic<bool>> Cancelled;
            QuadSpherePatchGeometry Geometry;
            std::atomic<int32> State{0};
        };

        std::shared_ptr<const QuadSphereTerrainSnapshot> _snapshot;
        std::shared_ptr<std::atomic<bool>> _cancelled;
        Array<QuadSpherePatchKey> _pending;
        Array<std::shared_ptr<Job>> _jobs;
        int32 _next = 0;
        int32 _workerLimit = 1;
        bool _active = false;

        void Schedule()
        {
            while (_jobs.Count() < _workerLimit && _next < _pending.Count())
            {
                auto job = std::make_shared<Job>();
                job->Key = _pending[_next++];
                job->Snapshot = _snapshot;
                job->Cancelled = _cancelled;
                _jobs.Add(job);
                Function<void()> work = [job]() mutable
                {
                    auto current = MoveTemp(job);
                    const bool built = QuadSphereTerrainGeometry::BuildPatch(current->Key, *current->Snapshot,
                                                                              *current->Cancelled, current->Geometry);
                    const bool cancelled = current->Cancelled->load(std::memory_order_relaxed);
                    current->Snapshot.reset();
                    current->Cancelled.reset();
                    current->State.store(built && !cancelled ? 1 : 2, std::memory_order_release);
                };
                Task::StartNew(work);
            }
        }

    public:
        ~QuadSphereGeometryBatch()
        {
            Cancel();
        }

        bool Begin(const std::shared_ptr<const QuadSphereTerrainSnapshot>& snapshot, Array<QuadSpherePatchKey>&& keys, int32 workerLimit)
        {
            if (_active)
            {
                LOG(Error, "Cannot start a quad sphere generation batch while another batch is active.");
                return false;
            }
#if PLATFORM_THREADS_LIMIT <= 1
            LOG(Error, "Quad sphere terrain generation requires worker threads on this platform.");
            return false;
#else
            if (!snapshot || snapshot->ChunksPerPatch < 1 || snapshot->ChunksPerPatch > 128 || snapshot->Resolution < 2 || snapshot->Resolution > 128)
            {
                LOG(Error, "Cannot schedule quad sphere geometry with invalid snapshot dimensions.");
                return false;
            }
            HashSet<QuadSpherePatchKey> uniqueKeys;
            for (const auto& key : keys)
            {
                if (key.Face < 0 || key.Face >= 6 || key.Level < 0 || key.Level > 10)
                {
                    LOG(Error, "Cannot schedule a quad sphere patch with an invalid face or LOD level.");
                    return false;
                }
                const int32 divisions = snapshot->ChunksPerPatch << key.Level;
                if (key.X < 0 || key.Y < 0 || key.X >= divisions || key.Y >= divisions)
                {
                    LOG(Error, "Cannot schedule a quad sphere patch with invalid coordinates.");
                    return false;
                }
                if (!uniqueKeys.Add(key))
                {
                    LOG(Error, "Cannot schedule the same quad sphere patch more than once in a batch.");
                    return false;
                }
            }
            _snapshot = snapshot;
            _cancelled = std::make_shared<std::atomic<bool>>(false);
            _pending = MoveTemp(keys);
            _next = 0;
            _workerLimit = Math::Clamp(workerLimit, 1, 32);
            _active = true;
            Schedule();
            return true;
#endif
        }

        QuadSphereGenerationResult TakeNext(QuadSpherePatchGeometry& output)
        {
            if (!_active)
            {
                LOG(Error, "Cannot poll an inactive quad sphere generation batch.");
                return QuadSphereGenerationResult::Failed;
            }
            for (int32 i = 0; i < _jobs.Count(); i++)
            {
                const auto& job = _jobs[i];
                const int32 state = job->State.load(std::memory_order_acquire);
                if (state == 0)
                    continue;
                if (state != 1)
                {
                    LOG(Error, "Quad sphere patch geometry generation failed or was cancelled.");
                    Cancel();
                    return QuadSphereGenerationResult::Failed;
                }
                output = MoveTemp(job->Geometry);
                _jobs.RemoveAt(i);
                Schedule();
                return QuadSphereGenerationResult::Ready;
            }
            if (_next == _pending.Count() && _jobs.IsEmpty())
            {
                Cancel();
                return QuadSphereGenerationResult::Complete;
            }
            return QuadSphereGenerationResult::Pending;
        }

        void Cancel()
        {
            if (_cancelled)
                _cancelled->store(true, std::memory_order_relaxed);
            _jobs.Clear();
            _pending.Clear();
            _snapshot.reset();
            _cancelled.reset();
            _next = 0;
            _active = false;
        }
    };
}

class QuadSphereRuntime
{
    using PatchMap = Dictionary<QuadSpherePatchKey, QuadSpherePatchResources*>;

    PatchMap _committed;
    PatchMap _staged;
    Dictionary<int32, QuadSpherePrefabChunk*> _prefabChunks;
    Dictionary<QuadSphereFoliageCellKey, QuadSphereFoliageCell*> _foliageCells;
    Dictionary<QuadSphereFoliageColliderKey, QuadSphereFoliageCollider*> _foliageColliders;
    HashSet<QuadSpherePatchKey> _desired;
    HashSet<Guid> _invalidBrushes;
    HashSet<int32> _failedPrefabAssets;
    HashSet<int32> _failedFoliageAssets;
    HashSet<int32> _invalidFoliageColliders;
    Array<Guid> _spawnedPrefabRoots;
    Array<QuadSpherePrefabConfig> _prefabConfigs;
    Array<QuadSpherePrefabConfig> _pendingPrefabConfigs;
    Array<QuadSphereFoliageConfig> _foliageConfigs;
    Array<QuadSphereFoliageConfig> _pendingFoliageConfigs;
    Array<QuadSphereFoliageCellKey> _foliageCellOrder;
    Array<QuadSphereFoliageCollider*> _foliageColliderPool;
    Array<Vector3> _cullCenters;
    Array<byte> _cullVisible;
    QuadSphereGeometryBatch _generation;
    std::shared_ptr<const QuadSphereTerrainSnapshot> _snapshot;
    std::shared_ptr<const QuadSphereTerrainSnapshot> _candidate;
    std::shared_ptr<const QuadSphereTerrainSnapshot> _failedBrushSnapshot;
    std::shared_ptr<const QuadSphereTerrainSnapshot> _prefabSnapshot;
    bool _inFlight = false;
    bool _generating = false;
    bool _brushUpdate = false;
    bool _prefabBrushesDirty = false;
    bool _rebuildRequested = false;
    bool _invalidPrefabTransform = false;
    bool _foliageSurfaceErrorLogged = false;
    bool _invalidFoliageTransform = false;
    bool _invalidFoliageCamera = false;
    bool _invalidFoliagePreviewDistance = false;
    bool _invalidFoliageClusterInterval = false;
    bool _foliageCellsLimited = false;
    bool _foliageCastShadows = true;
    bool _stopping = false;
    bool _candidateCollision = false;
    bool _committedCollision = false;
    bool _replaceAll = false;
    bool _lastSimpleSphere = false;
    bool _lastGenerateCollision = false;
    bool _castShadows = true;
    bool _initialRebuild = true;
    float _elapsed = 0.0f;
    float _brushCheckElapsed = 0.0f;
    float _prefabDiagnosticElapsed = 5.0f;
    float _foliageSelectionElapsed = 0.25f;
    float _foliageCollisionElapsed = 0.1f;
    int32 _prefabAttempts = 0;
    int32 _prefabSpawned = 0;
    int32 _prefabDestroyed = 0;
    int32 _prefabSpawnFailed = 0;
    int32 _prefabOperationDepth = 0;
    int32 _activeCalls = 0;
    double _cullRadius = 0.0;
    double _cullSphereRadius = 0.0;
    int32 _cullChunks = 0;
    int32 _foliageDivisions = 1;
    int32 _pendingFoliageDivisions = 1;
    int32 _foliageCellLimit = 0;

    static void ClearPatches(PatchMap& patches)
    {
        for (auto& pair : patches)
        {
            QuadSpherePatchInstallation::Destroy(*pair.Value);
            Delete(pair.Value);
        }
        patches.Clear();
    }

    void Abort()
    {
        _generation.Cancel();
        ClearPatches(_staged);
        _desired.Clear();
        _candidate.reset();
        _inFlight = false;
        _generating = false;
        _brushUpdate = false;
    }

    void Fail()
    {
        if (_brushUpdate)
            _failedBrushSnapshot = _candidate;
        Abort();
    }

    static bool PrefabInRange(const Vector3& position, const Vector3& target, double limit)
    {
        const double distance = (position - target).Length();
        return std::isfinite(limit) && limit > 0.0 && std::isfinite(distance) && distance <= limit;
    }

    double MaxPrefabSpawnDistance() const
    {
        double result = 0.0;
        for (const auto& config : _prefabConfigs)
        {
            const double distance = config.SpawnDistance;
            if (std::isfinite(distance) && distance > result)
                result = distance;
        }
        return result;
    }

    struct PrefabOperationScope
    {
        int32& Depth;
        explicit PrefabOperationScope(int32& depth) : Depth(depth) { Depth++; }
        ~PrefabOperationScope() { Depth--; }
    };

    void DestroyPrefab(QuadSpherePrefabCandidate& candidate)
    {
        if (auto* instance = candidate.Instance.Get())
        {
            instance->SetIsActive(false);
            if (auto* remaining = candidate.Instance.Get())
                remaining->DeleteObject();
            candidate.Instance = nullptr;
            _prefabDestroyed++;
        }
        if (candidate.InstanceId != Guid::Empty)
        {
            _spawnedPrefabRoots.Remove(candidate.InstanceId);
            candidate.InstanceId = Guid::Empty;
            _prefabBrushesDirty = true;
        }
    }

    void DestroyPrefabChunk(QuadSpherePrefabChunk& chunk)
    {
        for (auto& candidate : chunk.Candidates)
            DestroyPrefab(candidate);
    }

    void ClearPrefabs()
    {
        PrefabOperationScope scope(_prefabOperationDepth);
        for (auto& pair : _prefabChunks)
        {
            DestroyPrefabChunk(*pair.Value);
            Delete(pair.Value);
        }
        _prefabChunks.Clear();
        _spawnedPrefabRoots.Clear();
    }

    bool GetPrefabTarget(Vector3& target) const
    {
        auto* camera = Camera::GetMainCamera();
        if (!Engine::IsPlayMode() || !_prefabSnapshot || _prefabConfigs.IsEmpty() ||
            MaxPrefabSpawnDistance() <= 0.0 || !camera)
            return false;
        target = camera->GetPosition();
        return !target.IsNanOrInfinity();
    }

    void CleanupPrefabDistances(const QuadSphereActor& actor)
    {
        PrefabOperationScope scope(_prefabOperationDepth);
        Vector3 target;
        if (!GetPrefabTarget(target))
        {
            ClearPrefabs();
            return;
        }
        const Transform& transform = actor.GetTransform();
        for (auto& pair : _prefabChunks)
        {
            for (auto& candidate : pair.Value->Candidates)
            {
                const double distance = _prefabConfigs[candidate.Entry].SpawnDistance;
                auto* instance = candidate.Instance.Get();
                if (instance && !PrefabInRange(instance->GetPosition(), target, distance))
                    DestroyPrefab(candidate);
                if (_stopping)
                    return;
                if (!candidate.Instance.Get())
                {
                    if (candidate.InstanceId != Guid::Empty)
                        DestroyPrefab(candidate);
                    if (!PrefabInRange(transform.LocalToWorld(candidate.Surface), target, distance))
                        candidate.SpawnAttempted = false;
                }
            }
        }
    }

    bool TryFoliagePlacement(const QuadSphereFoliageConfig& config, int32 divisions, int32 cell, int32 entry,
                             int32 face, int32 x, int32 y, int32 attempt, Transform& placement)
    {
        constexpr double pi = 3.14159265358979323846;
        const auto& terrain = *_snapshot;
        const int32 seed = terrain.NoiseSeed;
        const double u = -1.0 + 2.0 * (x + QuadSphereTerrainGeometry::PlacementRandom(seed, cell, entry, attempt, 1)) / divisions;
        const double v = -1.0 + 2.0 * (y + QuadSphereTerrainGeometry::PlacementRandom(seed, cell, entry, attempt, 2)) / divisions;
        if (QuadSphereTerrainGeometry::PlacementRandom(seed, cell, entry, attempt, 3) >= std::pow(1.0 + u * u + v * v, -1.5))
            return false;
        const Double3 direction = QuadSphereTerrainGeometry::Direction(face, u, v);
        if ((config.Hemisphere == QuadSphereFoliageHemisphere::North && direction.Y < 0.0) ||
            (config.Hemisphere == QuadSphereFoliageHemisphere::South && direction.Y > 0.0))
            return false;
        if (config.MinTemperature > 0.0f || config.MaxTemperature < 1.0f)
        {
            const double temperature = 1.0 - std::asin(Math::Clamp(std::abs(direction.Y), 0.0, 1.0)) / (pi * 0.5);
            if (temperature < config.MinTemperature || temperature > config.MaxTemperature)
                return false;
        }

        // All five biome weights sum to one; the 32-bit placement random cannot reach their rounding error below one.
        const bool certainBiomeProbability = config.Biomes == QuadSpherePrefabBiomes::All && config.Probability >= 1.0f &&
            !config.SmoothSlope && config.NoiseDensityStrength <= 0.0f;
        const bool needsBaseHeight = !terrain.SimpleSphere &&
            (terrain.Brushes.HasItems() || (terrain.BeachWidth > 0.0 && !certainBiomeProbability));
        const double height = needsBaseHeight ? QuadSphereTerrainGeometry::BaseElevation(direction, terrain) : 0.0;
        const double beach = QuadSphereTerrainGeometry::BeachWeight(height, terrain);
        const double baseHeight = QuadSphereTerrainGeometry::FlattenBeach(height, beach, terrain);
        double removal = 0.0;
        if (!terrain.SimpleSphere && terrain.Brushes.HasItems())
        {
            const Vector3 worldPosition = terrain.PlanetTransform.LocalToWorld(Vector3(direction * Math::Max(0.01, terrain.Radius + baseHeight)));
            for (const auto& brush : terrain.Brushes)
            {
                if (brush.Mode == QuadSphereBrushMode::RemoveFoliage)
                    removal = Math::Max(removal, QuadSphereTerrainGeometry::BrushWeight(worldPosition, brush));
            }
        }
        if (removal >= 1.0 || (removal > 0.0 && QuadSphereTerrainGeometry::PlacementRandom(seed, cell, entry, attempt, 7) < removal))
            return false;

        Double3 surface;
        if (!TrySampleCommittedSurface(face, u, v, direction, surface))
        {
            if (!_foliageSurfaceErrorLogged)
                LOG(Warning, "Quad sphere foliage could not sample a committed terrain triangle; affected placements will be skipped.");
            _foliageSurfaceErrorLogged = true;
            return false;
        }
        const double altitude = surface.Length();
        if ((config.MinAltitude != 0.0 && altitude < config.MinAltitude) || (config.MaxAltitude != 0.0 && altitude > config.MaxAltitude))
            return false;
        double biomeWeight = 1.0;
        if (!certainBiomeProbability)
        {
            const Color32 biome = QuadSphereTerrainGeometry::BiomeWeights(direction, terrain);
            bool overridden = false;
            const Color32 weighted = QuadSphereTerrainGeometry::ApplyBrushBiomes(direction, baseHeight,
                QuadSphereTerrainGeometry::ApplyBeachWeight(biome, beach), terrain, &overridden);
            const byte weights[4] = { overridden ? weighted.R : biome.R, overridden ? weighted.G : biome.G,
                                      overridden ? weighted.B : biome.B, overridden ? weighted.A : biome.A };
            biomeWeight = 0.0;
            for (int32 i = 0; i < 4; i++)
                if (((int32)config.Biomes & (1 << i)) != 0)
                    biomeWeight += weights[i] / 255.0 * (overridden ? 1.0 : 1.0 - beach);
            if (((int32)config.Biomes & (int32)QuadSpherePrefabBiomes::Beach) != 0)
                biomeWeight += overridden ? (255 - weighted.R - weighted.G - weighted.B - weighted.A) / 255.0 : beach;
        }
        const double random = QuadSphereTerrainGeometry::PlacementRandom(seed, cell, entry, attempt, 4);
        if (biomeWeight <= 0.0 || random >= Math::Clamp((double)config.Probability, 0.0, 1.0) * biomeWeight)
            return false;

        const Double3 normal = QuadSphereTerrainGeometry::SurfaceNormal(direction, terrain);
        const double slope = terrain.SimpleSphere ? 0.0 : std::acos(Math::Clamp(Double3::Dot(normal, direction), -1.0, 1.0)) * (180.0 / pi);
        if (!std::isfinite(slope) || slope < config.MinSlope || slope > config.MaxSlope)
            return false;
        const double slopeWeight = config.SmoothSlope && config.MaxSlope > config.MinSlope ?
            (config.MaxSlope - slope) / ((double)config.MaxSlope - config.MinSlope) : 1.0;
        if (slopeWeight <= 0.0)
            return false;

        double densityNoise = 0.0;
        if (config.NoiseDensityStrength > 0.0f)
            densityNoise = QuadSphereTerrainGeometry::PlacementNoise(direction, config.NoiseFrequency, config.NoiseOctaves,
                config.NoiseSeedOffset, config.NoiseType, seed);
        const double densityWeight = 1.0 - Math::Clamp((double)config.NoiseDensityStrength, 0.0, 1.0) * (1.0 - densityNoise);
        const double probability = Math::Clamp((double)config.Probability, 0.0, 1.0) * biomeWeight * slopeWeight * densityWeight;
        if (random >= probability)
            return false;
        const float randomScale = (float)QuadSphereTerrainGeometry::PlacementRandom(seed, cell, entry, attempt, 6);
        float value = randomScale;
        if (config.NoiseScaleStrength > 0.0f)
        {
            const float scaleNoise = (float)QuadSphereTerrainGeometry::PlacementNoise(Double3::Normalize(surface),
                config.NoiseFrequency, config.NoiseOctaves, config.NoiseSeedOffset, config.NoiseType, seed);
            value += (scaleNoise - value) * Math::Clamp(config.NoiseScaleStrength, 0.0f, 1.0f);
        }
        const float scale = config.MinScale + (config.MaxScale - config.MinScale) * value;
        const Vector3 up = config.AlignToGroundNormal ? Vector3(normal) : Vector3::Normalize(Vector3(surface));
        const Vector3 reference = std::abs((double)up.Y) < 0.9 ? Vector3::UnitY : Vector3::UnitX;
        Float3 forward(Vector3::Normalize(Vector3::Cross(reference, up)));
        Quaternion yaw;
        Quaternion::RotationAxis(Float3(up), (float)(QuadSphereTerrainGeometry::PlacementRandom(seed, cell, entry, attempt, 5) * 2.0 * pi), yaw);
        forward = Float3::Transform(forward, yaw);
        placement = Transform(Vector3(surface), Quaternion::LookRotation(forward, Float3(up)), Float3(scale));
        return true;
    }

    bool FoliageFaceRange(int32 face, const Double3& camera, double localDistance,
                          int32& minX, int32& maxX, int32& minY, int32& maxY) const
    {
        minX = minY = 0;
        maxX = maxY = _foliageDivisions - 1;
        const double radius = _snapshot->Radius;
        const double length = camera.Length();
        if (std::isfinite(length) && std::isfinite(localDistance) && std::abs(length - radius) > localDistance)
            return false;
        if (!std::isfinite(length) || !std::isfinite(localDistance) || length < 0.000001)
            return true;
        const Double3 direction = camera / length;
        const double chord = Math::Min(2.0, (localDistance + std::abs(length - radius)) / radius + 0.000000001);
        double axis, u, v;
        switch (face)
        {
        case 0: axis = direction.X; u = -direction.Z; v = direction.Y; break;
        case 1: axis = -direction.X; u = direction.Z; v = direction.Y; break;
        case 2: axis = direction.Y; u = direction.X; v = -direction.Z; break;
        case 3: axis = -direction.Y; u = direction.X; v = direction.Z; break;
        case 4: axis = direction.Z; u = direction.X; v = direction.Y; break;
        default: axis = -direction.Z; u = -direction.X; v = direction.Y; break;
        }
        const double maximum = Math::Min(1.0, axis + chord);
        constexpr double minimumAxis = 0.5773502691896257;
        if (maximum < minimumAxis)
            return false;
        const double minimum = Math::Max(minimumAxis, axis - chord);
        const auto coordinateRange = [this, chord, minimum, maximum](double coordinate, int32& min, int32& max)
        {
            const double low = coordinate - chord, high = coordinate + chord;
            const double minValue = Math::Max(-1.0, Math::Min(low / minimum, low / maximum));
            const double maxValue = Math::Min(1.0, Math::Max(high / minimum, high / maximum));
            if (minValue > maxValue)
            {
                min = 1;
                max = 0;
                return;
            }
            min = Math::Clamp((int32)std::floor((minValue + 1.0) * 0.5 * _foliageDivisions) - 1, 0, _foliageDivisions - 1);
            max = Math::Clamp((int32)std::floor((maxValue + 1.0) * 0.5 * _foliageDivisions) + 1, 0, _foliageDivisions - 1);
        };
        coordinateRange(u, minX, maxX);
        coordinateRange(v, minY, maxY);
        return minX <= maxX && minY <= maxY;
    }

    bool FoliageBounds(const QuadSphereActor& actor, double& scale, double& minScale, double& cellBounds) const
    {
        if (!_snapshot)
            return false;
        const Transform& transform = actor.GetTransform();
        if (transform.Translation.IsNanOrInfinity() || transform.Orientation.IsNanOrInfinity() || transform.Scale.IsNanOrInfinity())
            return false;
        scale = Math::Max(std::abs((double)transform.Scale.X), Math::Max(std::abs((double)transform.Scale.Y), std::abs((double)transform.Scale.Z)));
        minScale = Math::Min(std::abs((double)transform.Scale.X), Math::Min(std::abs((double)transform.Scale.Y), std::abs((double)transform.Scale.Z)));
        const double factor = std::sqrt(2.0) / _snapshot->ChunksPerPatch + 0.00001;
        const double displacement = (_cullRadius - _snapshot->Radius * factor) / (1.0 + factor);
        cellBounds = (displacement + (_snapshot->Radius + displacement) * (std::sqrt(2.0) / _foliageDivisions + 0.00001)) * scale;
        return std::isfinite(cellBounds) && std::isfinite(minScale) && minScale > 0.0;
    }

    bool SelectFoliageCells(const QuadSphereActor& actor, const Vector3& camera,
                            Array<QuadSphereFoliageSelection>& result, bool& limited) const
    {
        PROFILE_CPU_NAMED("QuadSphere.Foliage.SelectCells");
        result.Clear();
        limited = false;
        double scale, minScale, cellBounds;
        if (camera.IsNanOrInfinity() || !FoliageBounds(actor, scale, minScale, cellBounds))
            return false;
        const Transform& transform = actor.GetTransform();
        const Double3 localCamera(transform.WorldToLocal(camera));
        const double padding = (camera.Length() + transform.Translation.Length() + _snapshot->Radius * scale + 1.0) * 0.00001;
        const int32 cellLimit = Math::Clamp(actor.MaxFoliageCells, 1, 4096);
        using SelectionSet = std::set<QuadSphereFoliageSelection>;
        SelectionSet nearest;
        std::vector<SelectionSet> perEntry(_foliageConfigs.Count());
        std::vector<int32> entryLimits(_foliageConfigs.Count(), 0);
        std::vector<bool> entryLimited(_foliageConfigs.Count(), false);
        std::priority_queue<QuadSphereFoliageRegion> regions;
        std::vector<QuadSphereFoliageRegion> deferred;
        const auto enqueue = [&](QuadSphereFoliageRegion region)
        {
            const double u = -1.0 + (region.MinX + region.MaxX + 1.0) / _foliageDivisions;
            const double v = -1.0 + (region.MinY + region.MaxY + 1.0) / _foliageDivisions;
            const Vector3 center(QuadSphereTerrainGeometry::Direction(region.Face, u, v) * _snapshot->Radius);
            const double width = (double)(region.MaxX - region.MinX) / _foliageDivisions;
            const double height = (double)(region.MaxY - region.MinY) / _foliageDivisions;
            const double radius = _snapshot->Radius * scale * std::sqrt(width * width + height * height) + padding;
            double lowerBound = (camera - transform.LocalToWorld(center)).Length() - radius;
            if (!std::isfinite(lowerBound))
                lowerBound = 0.0;
            if (lowerBound > region.MaxDistance)
                return;
            region.LowerBound = Math::Max(0.0, lowerBound);
            if (regions.size() >= 8192)
                deferred.push_back(region);
            else
                regions.push(region);
        };
        for (int32 entry = 0; entry < _foliageConfigs.Count(); entry++)
        {
            const auto& config = _foliageConfigs[entry];
            const int32 limit = config.MaxCells == -1 ? cellLimit : Math::Clamp(config.MaxCells, 0, cellLimit);
            const double distance = FoliageSpawnDistance(actor, config);
            if (limit == 0 || !std::isfinite(distance) || distance <= 0.0 || !config.Model.Get() ||
                !QuadSphereTerrainGeometry::ValidFoliageConfig(config))
                continue;
            entryLimits[entry] = limit;
            const double localDistance = (distance + cellBounds) / minScale;
            for (int32 face = 0; face < 6; face++)
            {
                QuadSphereFoliageRegion region{};
                region.Face = face;
                region.Entry = entry;
                region.MaxDistance = distance + cellBounds;
                if (FoliageFaceRange(face, localCamera, localDistance, region.MinX, region.MaxX, region.MinY, region.MaxY))
                    enqueue(region);
            }
        }
        while (!regions.empty() || !deferred.empty())
        {
            const bool fromStack = !deferred.empty();
            QuadSphereFoliageRegion region;
            if (fromStack)
            {
                region = deferred.back();
                deferred.pop_back();
            }
            else
            {
                region = regions.top();
                regions.pop();
            }
            if (limited && (int32)nearest.size() == cellLimit && region.LowerBound > nearest.rbegin()->Distance)
            {
                if (!fromStack)
                    break;
                continue;
            }
            auto& entryCells = perEntry[region.Entry];
            if (entryLimited[region.Entry] && (int32)entryCells.size() == entryLimits[region.Entry] &&
                region.LowerBound > entryCells.rbegin()->Distance)
                continue;
            if (region.MinX != region.MaxX || region.MinY != region.MaxY)
            {
                auto other = region;
                if (region.MaxX - region.MinX >= region.MaxY - region.MinY)
                {
                    region.MaxX = (region.MinX + region.MaxX) / 2;
                    other.MinX = region.MaxX + 1;
                }
                else
                {
                    region.MaxY = (region.MinY + region.MaxY) / 2;
                    other.MinY = region.MaxY + 1;
                }
                enqueue(region);
                enqueue(other);
                continue;
            }
            const double u = -1.0 + 2.0 * (region.MinX + 0.5) / _foliageDivisions;
            const double v = -1.0 + 2.0 * (region.MinY + 0.5) / _foliageDivisions;
            const Vector3 center(QuadSphereTerrainGeometry::Direction(region.Face, u, v) * _snapshot->Radius);
            const double distance = (camera - transform.LocalToWorld(center)).Length();
            if (!std::isfinite(distance) || distance > region.MaxDistance)
                continue;
            const QuadSphereFoliageSelection candidate{ distance, (region.Face * _foliageDivisions + region.MinY) * _foliageDivisions + region.MinX, region.Entry };
            if ((int32)entryCells.size() == entryLimits[region.Entry])
            {
                limited = entryLimited[region.Entry] = true;
                const auto farthest = *entryCells.rbegin();
                if (!(candidate < farthest))
                    continue;
                entryCells.erase(farthest);
                nearest.erase(farthest);
            }
            else if ((int32)nearest.size() == cellLimit)
            {
                limited = true;
                const auto farthest = *nearest.rbegin();
                if (!(candidate < farthest))
                    continue;
                nearest.erase(farthest);
                perEntry[farthest.Entry].erase(farthest);
            }
            nearest.insert(candidate);
            entryCells.insert(candidate);
        }
        for (const auto& cell : nearest)
            result.Add(cell);
        return true;
    }

    static void DestroyFoliageCell(QuadSphereFoliageCell& cell)
    {
        if (auto* renderer = cell.Renderer.Get())
        {
            renderer->SetIsActive(false);
            if (auto* remaining = cell.Renderer.Get())
                remaining->DeleteObject();
        }
        cell.Renderer = nullptr;
    }

    void RecycleFoliageColliders(const HashSet<int32>* cells = nullptr)
    {
        Array<QuadSphereFoliageColliderKey> removed;
        for (const auto& pair : _foliageColliders)
            if (!cells || cells->Contains(pair.Key.Cell.Cell))
                removed.Add(pair.Key);
        for (const auto& key : removed)
        {
            auto* item = _foliageColliders[key];
            _foliageColliders.Remove(key);
            if (auto* collider = item->Actor.Get())
            {
                collider->SetIsActive(false);
                _foliageColliderPool.Add(item);
            }
            else
                Delete(item);
        }
        if (!removed.IsEmpty())
            _foliageCollisionElapsed = 0.1f;
    }

    void ClearFoliage()
    {
        RecycleFoliageColliders();
        for (auto* item : _foliageColliderPool)
        {
            if (auto* collider = item->Actor.Get())
                collider->DeleteObject();
            Delete(item);
        }
        _foliageColliderPool.Clear();
        for (auto& pair : _foliageCells)
        {
            DestroyFoliageCell(*pair.Value);
            Delete(pair.Value);
        }
        _foliageCells.Clear();
        _foliageCellOrder.Clear();
        _foliageSelectionElapsed = 0.25f;
        _foliageCollisionElapsed = 0.1f;
        _foliageCellLimit = 0;
        _foliageCellsLimited = false;
    }

    void InvalidateFoliageForPatches(const Array<QuadSpherePatchKey>& patches)
    {
        if (_foliageCells.IsEmpty() || !_snapshot)
            return;
        HashSet<int32> liveCells;
        HashSet<int32> affectedCells;
        for (const auto& pair : _foliageCells)
            liveCells.Add(pair.Key.Cell);
        for (const auto& patch : patches)
        {
            const int32 divisions = _snapshot->ChunksPerPatch << patch.Level;
            const int32 minX = (int32)((int64)patch.X * _foliageDivisions / divisions);
            const int32 minY = (int32)((int64)patch.Y * _foliageDivisions / divisions);
            const int32 maxX = (int32)(((int64)(patch.X + 1) * _foliageDivisions - 1) / divisions);
            const int32 maxY = (int32)(((int64)(patch.Y + 1) * _foliageDivisions - 1) / divisions);
            const int64 count = (int64)(maxX - minX + 1) * (maxY - minY + 1);
            if (count <= liveCells.Count())
            {
                for (int32 y = minY; y <= maxY; y++)
                    for (int32 x = minX; x <= maxX; x++)
                    {
                        const int32 cell = (patch.Face * _foliageDivisions + y) * _foliageDivisions + x;
                        if (liveCells.Contains(cell))
                            affectedCells.Add(cell);
                    }
            }
            else
            {
                for (const auto& liveCell : liveCells)
                {
                    const int32 cell = liveCell.Item;
                    const int32 face = cell / (_foliageDivisions * _foliageDivisions);
                    const int32 y = cell / _foliageDivisions % _foliageDivisions, x = cell % _foliageDivisions;
                    if (face == patch.Face && x >= minX && x <= maxX && y >= minY && y <= maxY)
                        affectedCells.Add(cell);
                }
            }
            if (affectedCells.Count() == liveCells.Count())
                break;
        }
        if (!affectedCells.IsEmpty())
            RecycleFoliageColliders(&affectedCells);
        for (auto& pair : _foliageCells)
        {
            if (affectedCells.Contains(pair.Key.Cell))
            {
                DestroyFoliageCell(*pair.Value);
                Delete(pair.Value);
                pair.Value = New<QuadSphereFoliageCell>();
            }
        }
    }

    void ConfigureFoliage(Foliage& renderer, const QuadSphereActor& actor, int32 entry) const
    {
        const auto& config = _foliageConfigs[entry];
        const double distance = FoliageSpawnDistance(actor, config);
        const bool shadows = config.Shadows == QuadSphereFoliageShadows::Legacy ? actor.FoliageCastShadows :
            config.Shadows == QuadSphereFoliageShadows::Enabled;
        for (auto& type : renderer.FoliageTypes)
        {
            type.CullDistance = (float)Math::Min(distance, (double)MAX_float);
            type.CullDistanceRandomRange = 0.0f;
            type.ShadowsMode = shadows ? ShadowsCastingMode::All : ShadowsCastingMode::None;
        }
        renderer.UpdateCullDistance();
    }

    void InstallFoliageCell(QuadSphereActor& actor, const QuadSphereFoliageCellKey& key, QuadSphereFoliageCell& cell, int32& budget)
    {
        PROFILE_CPU_NAMED("QuadSphere.Foliage.InstallInstances");
        if (cell.Placements.IsEmpty())
        {
            cell.Complete = true;
            return;
        }
        if (budget <= 0)
            return;
        auto* model = _foliageConfigs[key.Entry].Model.Get();
        if (!model || model->LastLoadFailed() || !model->IsLoaded())
            return;
        auto* renderer = cell.Renderer.Get();
        if (!renderer)
        {
            renderer = New<Foliage>();
            cell.Renderer = renderer;
            cell.Installed = 0;
            cell.Published = 0;
            renderer->HideFlags = HideFlags::FullyHidden;
            renderer->SetIsActive(false);
            renderer->SetParent(&actor, false, false);
            if (_stopping || _rebuildRequested || !cell.Renderer.Get())
            {
                DestroyFoliageCell(cell);
                return;
            }
            renderer->SetLocalTransform(Transform::Identity);
            if (_stopping || _rebuildRequested || !cell.Renderer.Get())
            {
                DestroyFoliageCell(cell);
                return;
            }
            renderer->AddFoliageType(model);
            if (renderer->FoliageTypes.Count() != 1)
            {
                LOG(Error, "Failed to create foliage type for quad sphere entry {0}.", key.Entry);
                DestroyFoliageCell(cell);
                cell.Complete = true;
                return;
            }
            ConfigureFoliage(*renderer, actor, key.Entry);
        }
        const auto& config = _foliageConfigs[key.Entry];
        const float cullDistance = (float)Math::Min(FoliageSpawnDistance(actor, config), (double)MAX_float);
        const bool shadows = config.Shadows == QuadSphereFoliageShadows::Legacy ? actor.FoliageCastShadows :
            config.Shadows == QuadSphereFoliageShadows::Enabled;
        const auto& type = renderer->FoliageTypes[0];
        if (type.CullDistance != cullDistance || type.CullDistanceRandomRange != 0.0f ||
            type.ShadowsMode != (shadows ? ShadowsCastingMode::All : ShadowsCastingMode::None))
            ConfigureFoliage(*renderer, actor, key.Entry);
        while (cell.Installed < cell.Placements.Count() && budget > 0)
        {
            FoliageInstance instance{};
            instance.Type = 0;
            instance.Transform = cell.Placements[cell.Installed++];
            renderer->AddInstance(instance);
            budget--;
        }
        cell.Complete = cell.Installed == cell.Placements.Count();
    }

    void PublishFoliageCell(QuadSphereFoliageCell& cell, double now, double interval)
    {
        if (cell.Installed == cell.Published ||
            (cell.Published != 0 && !cell.Complete && now - cell.LastPublishedTime < interval))
            return;
        auto* renderer = cell.Renderer.Get();
        if (!renderer)
            return;
        PROFILE_CPU_NAMED("QuadSphere.Foliage.RebuildClusters");
        renderer->RebuildClusters();
        if (_stopping || _rebuildRequested || cell.Renderer.Get() != renderer)
            return;
        cell.Published = cell.Installed;
        cell.LastPublishedTime = now;
        renderer->SetIsActive(true);
    }

    void UpdateFoliageColliders(QuadSphereActor& actor, const Vector3& camera)
    {
        PROFILE_CPU_NAMED("QuadSphere.Foliage.Colliders");
        double scale, minScale, cellBounds;
        if (!FoliageBounds(actor, scale, minScale, cellBounds))
        {
            if (!_invalidFoliageTransform)
                LOG(Error, "Cannot update quad sphere foliage colliders with an invalid planet transform or bounds.");
            _invalidFoliageTransform = true;
            RecycleFoliageColliders();
            return;
        }
        const Transform& transform = actor.GetTransform();
        std::vector<std::vector<QuadSphereFoliageCollisionCandidate>> nearest(_foliageConfigs.Count());
        for (const auto& pair : _foliageCells)
        {
            const auto& cell = *pair.Value;
            auto* renderer = cell.Renderer.Get();
            if (!renderer || !renderer->GetIsActive())
                continue;
            const auto& config = _foliageConfigs[pair.Key.Entry];
            const int32 limit = Math::Clamp(config.MaxColliders == -1 ? actor.MaxFoliageColliders : config.MaxColliders, 0, 1024);
            const double spawnDistance = FoliageSpawnDistance(actor, config);
            const double collisionDistance = config.CollisionDistance == -1.0 ? actor.FoliageCollisionDistance : config.CollisionDistance;
            if (limit == 0 || !std::isfinite(spawnDistance) || !std::isfinite(collisionDistance) || spawnDistance <= 0.0 || collisionDistance <= 0.0)
                continue;
            if (config.CollisionSize.IsNanOrInfinity() || config.CollisionOffset.IsNanOrInfinity())
            {
                if (_invalidFoliageColliders.Add(pair.Key.Entry))
                    LOG(Warning, "Quad sphere foliage entry {0} has a non-finite collision box size or offset.", pair.Key.Entry);
                continue;
            }
            if (config.CollisionSize.X <= 0.0f || config.CollisionSize.Y <= 0.0f || config.CollisionSize.Z <= 0.0f)
                continue;
            const double distanceLimit = Math::Min(collisionDistance, spawnDistance);
            const int32 face = pair.Key.Cell / (_foliageDivisions * _foliageDivisions);
            const int32 y = pair.Key.Cell / _foliageDivisions % _foliageDivisions, x = pair.Key.Cell % _foliageDivisions;
            const double u = -1.0 + 2.0 * (x + 0.5) / _foliageDivisions;
            const double v = -1.0 + 2.0 * (y + 0.5) / _foliageDivisions;
            const Vector3 center(QuadSphereTerrainGeometry::Direction(face, u, v) * _snapshot->Radius);
            const double centerDistance = (camera - transform.LocalToWorld(center)).Length();
            if (!std::isfinite(centerDistance) || centerDistance > distanceLimit + cellBounds)
                continue;
            auto& candidates = nearest[pair.Key.Entry];
            for (int32 i = 0; i < cell.Published; i++)
            {
                const Transform& placement = cell.Placements[i];
                const double distance = (camera - transform.LocalToWorld(placement.Translation)).Length();
                if (!std::isfinite(distance) || distance > distanceLimit)
                    continue;
                const QuadSphereFoliageCollisionCandidate candidate{ { pair.Key, i }, placement, distance };
                auto position = std::lower_bound(candidates.begin(), candidates.end(), candidate);
                if (position - candidates.begin() >= limit)
                    continue;
                candidates.insert(position, candidate);
                if ((int32)candidates.size() > limit)
                    candidates.pop_back();
            }
        }
        HashSet<QuadSphereFoliageColliderKey> desired;
        for (const auto& candidates : nearest)
            for (const auto& candidate : candidates)
                desired.Add(candidate.Key);
        Array<QuadSphereFoliageColliderKey> removed;
        for (const auto& pair : _foliageColliders)
            if (!desired.Contains(pair.Key) || !pair.Value->Actor.Get())
                removed.Add(pair.Key);
        for (const auto& key : removed)
        {
            auto* item = _foliageColliders[key];
            _foliageColliders.Remove(key);
            if (auto* collider = item->Actor.Get())
            {
                collider->SetIsActive(false);
                _foliageColliderPool.Add(item);
            }
            else
                Delete(item);
            if (_stopping || _rebuildRequested)
                return;
        }
        for (const auto& candidates : nearest)
        {
            for (const auto& candidate : candidates)
            {
                if (_foliageColliders.ContainsKey(candidate.Key))
                    continue;
                QuadSphereFoliageCollider* item = nullptr;
                while (!_foliageColliderPool.IsEmpty() && !item)
                {
                    auto* pooled = _foliageColliderPool.Pop();
                    if (pooled->Actor.Get())
                        item = pooled;
                    else
                        Delete(pooled);
                }
                const bool create = !item;
                if (create)
                {
                    item = New<QuadSphereFoliageCollider>();
                    item->Actor = New<BoxCollider>();
                }
                _foliageColliders.Add(candidate.Key, item);
                auto* collider = item->Actor.Get();
                if (create)
                {
                    collider->HideFlags = HideFlags::FullyHidden;
                    collider->SetIsActive(false);
                    collider->SetParent(&actor, false, false);
                    if (_stopping || _rebuildRequested || !item->Actor.Get())
                    {
                        _foliageColliders.Remove(candidate.Key);
                        if (auto* remaining = item->Actor.Get())
                            remaining->DeleteObject();
                        Delete(item);
                        return;
                    }
                }
                const auto& config = _foliageConfigs[candidate.Key.Cell.Entry];
                collider->SetSize(config.CollisionSize);
                if (_stopping || _rebuildRequested || !item->Actor.Get())
                {
                    _foliageColliders.Remove(candidate.Key);
                    if (auto* remaining = item->Actor.Get())
                        remaining->DeleteObject();
                    Delete(item);
                    return;
                }
                const Transform& placement = candidate.Placement;
                collider->SetLocalTransform(Transform(placement.LocalToWorld(Vector3(config.CollisionOffset)), placement.Orientation, placement.Scale));
                if (_stopping || _rebuildRequested || !item->Actor.Get())
                {
                    _foliageColliders.Remove(candidate.Key);
                    if (auto* remaining = item->Actor.Get())
                        remaining->DeleteObject();
                    Delete(item);
                    return;
                }
                collider->SetIsActive(true);
                if (_stopping || _rebuildRequested)
                    return;
            }
        }
    }

    void UpdateFoliage(QuadSphereActor& actor)
    {
        PROFILE_CPU_NAMED("QuadSphere.Foliage.Update");
        Vector3 cameraPosition;
        if (!_snapshot || _foliageConfigs.IsEmpty() || (!Engine::IsPlayMode() && !actor.PreviewFoliageInEditor) ||
            !TryGetFoliageTarget(actor, cameraPosition))
        {
            _invalidFoliageCamera = false;
            ClearFoliage();
            return;
        }
        if (cameraPosition.IsNanOrInfinity())
        {
            if (!_invalidFoliageCamera)
                LOG(Error, "Cannot stream quad sphere foliage with a non-finite target position.");
            _invalidFoliageCamera = true;
            ClearFoliage();
            return;
        }
        _invalidFoliageCamera = false;
        const bool invalidPreviewDistance = !Engine::IsPlayMode() &&
            (!std::isfinite(actor.EditorFoliagePreviewDistance) || actor.EditorFoliagePreviewDistance < 0.0);
        if (invalidPreviewDistance && !_invalidFoliagePreviewDistance)
            LOG(Warning, "Quad sphere editor foliage preview distance must be finite and non-negative; the fallback distance will be ignored.");
        _invalidFoliagePreviewDistance = invalidPreviewDistance;
        const int32 cellLimit = Math::Clamp(actor.MaxFoliageCells, 1, 4096);
        _foliageSelectionElapsed += Time::GetUnscaledDeltaTime();
        if (_foliageSelectionElapsed >= 0.25f || _foliageCellLimit != cellLimit)
        {
            Array<QuadSphereFoliageSelection> selected;
            bool limited;
            if (!SelectFoliageCells(actor, cameraPosition, selected, limited))
            {
                if (!_invalidFoliageTransform)
                    LOG(Error, "Cannot stream quad sphere foliage with an invalid planet transform or bounds.");
                _invalidFoliageTransform = true;
                ClearFoliage();
                return;
            }
            _invalidFoliageTransform = false;
            _foliageSelectionElapsed = 0.0f;
            _foliageCellLimit = cellLimit;
            _foliageCellsLimited = limited;
            HashSet<QuadSphereFoliageCellKey> desired;
            _foliageCellOrder.Clear();
            for (const auto& item : selected)
            {
                const QuadSphereFoliageCellKey key{ item.Cell, item.Entry };
                desired.Add(key);
                _foliageCellOrder.Add(key);
                if (!_foliageCells.ContainsKey(key))
                    _foliageCells.Add(key, New<QuadSphereFoliageCell>());
                else if (auto* renderer = _foliageCells[key]->Renderer.Get())
                {
                    const double distance = FoliageSpawnDistance(actor, _foliageConfigs[key.Entry]);
                    const float cullDistance = (float)Math::Min(distance, (double)MAX_float);
                    if (renderer->FoliageTypes.HasItems() && renderer->FoliageTypes[0].CullDistance != cullDistance)
                        ConfigureFoliage(*renderer, actor, key.Entry);
                }
            }
            Array<QuadSphereFoliageCellKey> removed;
            for (const auto& pair : _foliageCells)
                if (!desired.Contains(pair.Key))
                    removed.Add(pair.Key);
            HashSet<int32> removedCells;
            for (const auto& key : removed)
                removedCells.Add(key.Cell);
            if (!removedCells.IsEmpty())
                RecycleFoliageColliders(&removedCells);
            for (const auto& key : removed)
            {
                auto* cell = _foliageCells[key];
                DestroyFoliageCell(*cell);
                Delete(cell);
                _foliageCells.Remove(key);
                if (_stopping || _rebuildRequested)
                    return;
            }
        }
        if (_foliageCastShadows != actor.FoliageCastShadows)
        {
            _foliageCastShadows = actor.FoliageCastShadows;
            for (const auto& pair : _foliageCells)
                if (auto* renderer = pair.Value->Renderer.Get())
                    if (_foliageConfigs[pair.Key.Entry].Shadows == QuadSphereFoliageShadows::Legacy)
                        ConfigureFoliage(*renderer, actor, pair.Key.Entry);
        }
        int32 budget = Math::Clamp(actor.FoliageAttemptsPerUpdate, 1, 100000);
        int32 installBudget = budget;
        for (const auto& key : _foliageCellOrder)
        {
            auto& cell = *_foliageCells[key];
            if (cell.Complete && cell.Installed > 0 && !cell.Renderer.Get())
            {
                cell.Installed = 0;
                cell.Published = 0;
                cell.Complete = false;
            }
            if (cell.Complete)
                continue;
            const auto& config = _foliageConfigs[key.Entry];
            auto* model = config.Model.Get();
            if (!model || model->LastLoadFailed())
            {
                if (model && model->LastLoadFailed() && _failedFoliageAssets.Add(key.Entry))
                    LOG(Warning, "Quad sphere foliage entry {0} failed to load asset {1}.", key.Entry, model->GetID());
                cell.GenerationComplete = cell.Complete = true;
                continue;
            }
            if (!model->IsLoaded())
                continue;
            if (cell.AttemptCount < 0)
            {
                const double side = 2.0 / _foliageDivisions;
                const double radius = _snapshot->Radius / 1000.0;
                const double expected = config.Density * radius * radius * side * side;
                if (!std::isfinite(expected) || expected > (double)MAX_int32 - 1.0)
                {
                    if (_failedFoliageAssets.Add(key.Entry))
                        LOG(Warning, "Quad sphere foliage entry {0} produces too many attempts per cell; reduce density or increase foliage divisions.", key.Entry);
                    cell.GenerationComplete = cell.Complete = true;
                    continue;
                }
                cell.AttemptCount = (int32)expected;
                if (QuadSphereTerrainGeometry::PlacementRandom(_snapshot->NoiseSeed, key.Cell, key.Entry, -1, 0) < expected - cell.AttemptCount)
                    cell.AttemptCount++;
            }
            const int32 face = key.Cell / (_foliageDivisions * _foliageDivisions);
            const int32 y = key.Cell / _foliageDivisions % _foliageDivisions, x = key.Cell % _foliageDivisions;
            if (cell.Attempt < cell.AttemptCount && budget > 0)
            {
                PROFILE_CPU_NAMED("QuadSphere.Foliage.GeneratePlacements");
                while (cell.Attempt < cell.AttemptCount && budget > 0)
                {
                    Transform placement;
                    if (TryFoliagePlacement(config, _foliageDivisions, key.Cell, key.Entry, face, x, y, cell.Attempt++, placement))
                        cell.Placements.Add(placement);
                    budget--;
                }
            }
            cell.GenerationComplete = cell.Attempt == cell.AttemptCount;
            if (cell.GenerationComplete)
                InstallFoliageCell(actor, key, cell, installBudget);
            if (_stopping || _rebuildRequested)
                return;
            if (budget == 0 && installBudget == 0)
                break;
        }
        const double now = Platform::GetTimeSeconds();
        const bool invalidClusterInterval = !std::isfinite(actor.FoliageClusterRebuildInterval) || actor.FoliageClusterRebuildInterval < 0.0f;
        if (invalidClusterInterval && !_invalidFoliageClusterInterval)
            LOG(Warning, "Quad sphere foliage cluster rebuild interval must be finite and non-negative; publishing every batch.");
        _invalidFoliageClusterInterval = invalidClusterInterval;
        const double clusterInterval = invalidClusterInterval ? 0.0 : actor.FoliageClusterRebuildInterval;
        for (const auto& key : _foliageCellOrder)
        {
            PublishFoliageCell(*_foliageCells[key], now, clusterInterval);
            if (_stopping || _rebuildRequested)
                return;
        }
        if (!Engine::IsPlayMode())
            RecycleFoliageColliders();
        else
        {
            _foliageCollisionElapsed += Time::GetUnscaledDeltaTime();
            if (_foliageCollisionElapsed >= 0.1f)
            {
                _foliageCollisionElapsed = 0.0f;
                UpdateFoliageColliders(actor, cameraPosition);
            }
        }
    }

    bool UpdatePrefabs(QuadSphereActor& actor)
    {
        PrefabOperationScope scope(_prefabOperationDepth);
        Vector3 target;
        if (!GetPrefabTarget(target))
        {
            ClearPrefabs();
            return false;
        }
        CleanupPrefabDistances(actor);
        if (_stopping)
            return false;
        const Transform& transform = actor.GetTransform();
        const double scale = Math::Max(std::abs((double)transform.Scale.X),
            Math::Max(std::abs((double)transform.Scale.Y), std::abs((double)transform.Scale.Z)));
        const double bounds = _cullRadius * scale;
        if (transform.Translation.IsNanOrInfinity() || transform.Orientation.IsNanOrInfinity() ||
            transform.Scale.IsNanOrInfinity() || transform.Scale.X == 0.0f || transform.Scale.Y == 0.0f ||
            transform.Scale.Z == 0.0f || !std::isfinite(bounds))
        {
            if (!_invalidPrefabTransform)
                LOG(Error, "Cannot stream quad sphere prefabs with an invalid planet transform or bounds.");
            _invalidPrefabTransform = true;
            ClearPrefabs();
            return false;
        }
        _invalidPrefabTransform = false;
        const double maxSpawnDistance = MaxPrefabSpawnDistance();
        Array<int32> removed;
        for (int32 root = 0; root < _cullCenters.Count(); root++)
        {
            const double distance = (target - transform.LocalToWorld(_cullCenters[root])).Length();
            if (std::isfinite(distance) && distance <= maxSpawnDistance + bounds)
            {
                if (!_prefabChunks.ContainsKey(root))
                    _prefabChunks.Add(root, New<QuadSpherePrefabChunk>());
            }
            else if (_prefabChunks.ContainsKey(root))
                removed.Add(root);
        }
        for (const int32 root : removed)
        {
            auto* chunk = _prefabChunks[root];
            DestroyPrefabChunk(*chunk);
            Delete(chunk);
            _prefabChunks.Remove(root);
            if (_stopping)
                return false;
        }

        const int32 chunks = _prefabSnapshot->ChunksPerPatch;
        const int32 seed = _prefabSnapshot->NoiseSeed;
        int32 budget = Math::Clamp(actor.PrefabAttemptsPerUpdate, 1, 100000);
        for (auto& pair : _prefabChunks)
        {
            auto& chunk = *pair.Value;
            const int32 root = pair.Key;
            const int32 face = root / (chunks * chunks), y = root / chunks % chunks, x = root % chunks;
            while (chunk.Entry < _prefabConfigs.Count() && budget > 0)
            {
                const auto& config = _prefabConfigs[chunk.Entry];
                auto* prefab = config.Prefab.Get();
                if (!prefab || prefab->LastLoadFailed() || !QuadSphereTerrainGeometry::ValidPrefabConfig(config))
                {
                    if (prefab && prefab->LastLoadFailed() && _failedPrefabAssets.Add(chunk.Entry))
                        LOG(Warning, "Quad sphere prefab entry {0} failed to load asset {1}.", chunk.Entry, prefab->GetID());
                    chunk.Entry++;
                    chunk.AttemptCount = -1;
                    continue;
                }
                if (!prefab->IsLoaded())
                    break;
                if (chunk.AttemptCount < 0)
                {
                    const double side = 2.0 / chunks;
                    const double radius = _prefabSnapshot->Radius / 2000000.0;
                    const double expected = config.Density * radius * radius * side * side;
                    if (!std::isfinite(expected) || expected > (double)MAX_int32 - 1.0)
                    {
                        LOG(Warning, "Quad sphere prefab density produces too many attempts for base chunk {0}.", root);
                        chunk.Entry++;
                        continue;
                    }
                    chunk.AttemptCount = (int32)expected;
                    if (QuadSphereTerrainGeometry::PlacementRandom(seed, root, chunk.Entry, -1, 0) < expected - chunk.AttemptCount)
                        chunk.AttemptCount++;
                    chunk.Attempt = 0;
                }
                if (chunk.Attempt >= chunk.AttemptCount)
                {
                    chunk.Entry++;
                    chunk.AttemptCount = -1;
                    continue;
                }
                const int32 attempt = chunk.Attempt++;
                _prefabAttempts++;
                Vector3 surface;
                if (QuadSphereTerrainGeometry::TryPrefabPlacement(*_prefabSnapshot, config, root, chunk.Entry, face, x, y, attempt, surface))
                {
                    QuadSpherePrefabCandidate candidate;
                    candidate.Surface = surface;
                    candidate.Entry = chunk.Entry;
                    candidate.Yaw = (float)(QuadSphereTerrainGeometry::PlacementRandom(seed, root, chunk.Entry, attempt, 5) * 6.28318530717958647692);
                    chunk.Candidates.Add(MoveTemp(candidate));
                }
                budget--;
            }
            if (budget == 0)
                break;
        }

        int32 spawnBudget = Math::Clamp(actor.PrefabAttemptsPerUpdate, 1, 100000);
        for (auto& pair : _prefabChunks)
        {
            for (auto& candidate : pair.Value->Candidates)
            {
                if (spawnBudget == 0)
                    break;
                const Vector3 position = transform.LocalToWorld(candidate.Surface);
                if (candidate.Instance.Get() || candidate.SpawnAttempted ||
                    !PrefabInRange(position, target, _prefabConfigs[candidate.Entry].SpawnDistance))
                    continue;
                auto* prefab = _prefabConfigs[candidate.Entry].Prefab.Get();
                if (!prefab || !prefab->IsLoaded() || prefab->LastLoadFailed())
                    continue;
                candidate.SpawnAttempted = true;
                Vector3 up = position - transform.LocalToWorld(Vector3::Zero);
                const double upLengthSquared = up.LengthSquared();
                if (up.IsNanOrInfinity() || !std::isfinite(upLengthSquared) || upLengthSquared < 0.000000000001)
                {
                    LOG(Warning, "Cannot spawn a quad sphere prefab with an invalid radial direction.");
                    spawnBudget--;
                    continue;
                }
                up.Normalize();
                const Vector3 reference = std::abs((double)up.Y) < 0.9 ? Vector3::UnitY : Vector3::UnitX;
                Float3 forward(Vector3::Normalize(Vector3::Cross(reference, up)));
                Quaternion yaw;
                Quaternion::RotationAxis(Float3(up), candidate.Yaw, yaw);
                forward = Float3::Transform(forward, yaw);
                const Quaternion rotation = Quaternion::LookRotation(forward, Float3(up));
                Actor* instance = PrefabManager::SpawnPrefab(prefab, (Actor*)nullptr);
                if (!instance)
                {
                    LOG(Error, "Failed to spawn quad sphere prefab {0}.", prefab->GetID());
                    _prefabSpawnFailed++;
                    spawnBudget--;
                    continue;
                }
                candidate.Instance = instance;
                candidate.InstanceId = instance->GetID();
                _spawnedPrefabRoots.Add(candidate.InstanceId);
                _prefabBrushesDirty = true;
                spawnBudget--;
                if (_stopping || _rebuildRequested)
                    return false;
                instance->SetPosition(position);
                if (_stopping || _rebuildRequested)
                    return false;
                instance = candidate.Instance.Get();
                if (!instance)
                {
                    LOG(Warning, "Quad sphere prefab was destroyed while setting its position.");
                    DestroyPrefab(candidate);
                    _prefabSpawnFailed++;
                    continue;
                }
                instance->SetOrientation(rotation);
                if (_stopping || _rebuildRequested)
                    return false;
                instance = candidate.Instance.Get();
                if (!instance)
                {
                    LOG(Warning, "Quad sphere prefab was destroyed while setting its orientation.");
                    DestroyPrefab(candidate);
                    _prefabSpawnFailed++;
                    continue;
                }
                instance->SetParent(&actor, true, false);
                if (_stopping || _rebuildRequested)
                    return false;
                instance = candidate.Instance.Get();
                if (!instance || instance->GetParent() != &actor)
                {
                    LOG(Warning, "Quad sphere prefab was destroyed or detached while being spawned.");
                    DestroyPrefab(candidate);
                    _prefabSpawnFailed++;
                    continue;
                }
                _prefabSpawned++;
            }
            if (spawnBudget == 0)
                break;
        }
        for (const auto& pair : _prefabChunks)
        {
            const auto& chunk = *pair.Value;
            if (chunk.Entry < _prefabConfigs.Count())
                return true;
            for (const auto& candidate : chunk.Candidates)
            {
                auto* prefab = _prefabConfigs[candidate.Entry].Prefab.Get();
                if (!candidate.Instance.Get() && !candidate.SpawnAttempted && prefab && !prefab->LastLoadFailed() &&
                    PrefabInRange(transform.LocalToWorld(candidate.Surface), target, _prefabConfigs[candidate.Entry].SpawnDistance))
                    return true;
            }
        }
        return false;
    }

    void UpdatePrefabDiagnostics(const QuadSphereActor& actor)
    {
        if (!Engine::IsPlayMode() || !actor.PrefabDiagnostics)
        {
            _prefabDiagnosticElapsed = 5.0f;
            _prefabAttempts = _prefabSpawned = _prefabDestroyed = _prefabSpawnFailed = 0;
            return;
        }
        _prefabDiagnosticElapsed += Time::GetUnscaledDeltaTime();
        if (_prefabDiagnosticElapsed < 5.0f)
            return;
        _prefabDiagnosticElapsed = 0.0f;
        int32 candidates = 0, alive = 0, pending = 0;
        for (const auto& pair : _prefabChunks)
        {
            const auto& chunk = *pair.Value;
            pending += chunk.Entry < _prefabConfigs.Count() ? 1 : 0;
            candidates += chunk.Candidates.Count();
            for (const auto& candidate : chunk.Candidates)
                alive += candidate.Instance.Get() ? 1 : 0;
        }
        LOG(Info, "Quad sphere '{0}' prefabs: chunks={1}, pending={2}, candidates={3}, alive={4}, attempts={5}, spawned={6}, destroyed={7}, failed={8}.",
            actor.GetName(), _prefabChunks.Count(), pending, candidates, alive, _prefabAttempts, _prefabSpawned, _prefabDestroyed, _prefabSpawnFailed);
        int32 foliagePending = 0;
        int64 foliageInstances = 0, foliagePublished = 0;
        for (const auto& pair : _foliageCells)
        {
            foliagePending += pair.Value->Complete ? 0 : 1;
            foliageInstances += pair.Value->Installed;
            foliagePublished += pair.Value->Published;
        }
        LOG(Info, "Quad sphere '{0}' foliage: configs={1}, cells={2}, cellLimitReached={3}, pending={4}, instances={5}, published={6}, activeColliders={7}, pooledColliders={8}.",
            actor.GetName(), _foliageConfigs.Count(), _foliageCells.Count(), _foliageCellsLimited,
            foliagePending, foliageInstances, foliagePublished, _foliageColliders.Count(), _foliageColliderPool.Count());
        _prefabAttempts = _prefabSpawned = _prefabDestroyed = _prefabSpawnFailed = 0;
    }

    static bool TryGetTargetPosition(const QuadSphereActor& actor, Vector3& position)
    {
        if (TryGetEditorViewPosition(position))
            return true;
        Actor* target = actor.LodTarget.Get();
        if (!target)
            target = Camera::GetMainCamera();
        if (!target)
            return false;
        position = target->GetPosition();
        return true;
    }

    void InitializeCullBounds(const QuadSphereTerrainSnapshot& snapshot)
    {
        const int32 chunks = snapshot.ChunksPerPatch;
        if (_cullChunks != chunks || _cullSphereRadius != snapshot.Radius || _cullCenters.IsEmpty())
        {
            const int32 count = 6 * chunks * chunks;
            _cullCenters.Resize(count);
            _cullVisible.Resize(count);
            for (int32 face = 0; face < 6; face++)
                for (int32 y = 0; y < chunks; y++)
                    for (int32 x = 0; x < chunks; x++)
                    {
                        const int32 root = (face * chunks + y) * chunks + x;
                        const double u = -1.0 + 2.0 * (x + 0.5) / chunks;
                        const double v = -1.0 + 2.0 * (y + 0.5) / chunks;
                        _cullCenters[root] = Vector3(QuadSphereTerrainGeometry::Direction(face, u, v) * snapshot.Radius);
                        _cullVisible[root] = 0;
                    }
            _cullChunks = chunks;
            _cullSphereRadius = snapshot.Radius;
        }
        const double displacement = QuadSphereTerrainGeometry::BrushDisplacement(snapshot) + snapshot.SkirtDepth + 0.01;
        // Bound both radial displacement and the furthest direction in a root face square.
        _cullRadius = displacement + (snapshot.Radius + displacement) * (std::sqrt(2.0) / chunks + 0.00001);
    }

    void UpdatePatchVisibility(const QuadSphereActor& actor)
    {
        Vector3 target = Vector3::Zero;
        const bool culling = _snapshot && _cullChunks == _snapshot->ChunksPerPatch &&
            _cullCenters.Count() == 6 * _cullChunks * _cullChunks && _cullVisible.Count() == _cullCenters.Count() &&
            std::isfinite(actor.ChunkCullDistance) && actor.ChunkCullDistance > 0.0 && TryGetTargetPosition(actor, target);
        const double hysteresis = std::isfinite(actor.ChunkCullHysteresis) ? Math::Clamp(actor.ChunkCullHysteresis, 0.0, 1.0) : 0.0;
        const Transform& transform = actor.GetTransform();
        const double scale = Math::Max(std::abs((double)transform.Scale.X),
            Math::Max(std::abs((double)transform.Scale.Y), std::abs((double)transform.Scale.Z)));
        const double worldRadius = _cullRadius * scale;
        const bool validBounds = culling && !target.IsNanOrInfinity() && !transform.Translation.IsNanOrInfinity() &&
            !transform.Orientation.IsNanOrInfinity() && !transform.Scale.IsNanOrInfinity() &&
            transform.Scale.X != 0.0f && transform.Scale.Y != 0.0f && transform.Scale.Z != 0.0f && std::isfinite(worldRadius);
        for (int32 i = 0; i < _cullCenters.Count() && i < _cullVisible.Count(); i++)
        {
            bool visible = true;
            if (validBounds)
            {
                const double distance = (target - transform.LocalToWorld(_cullCenters[i])).Length();
                const double margin = _cullVisible[i] ? actor.ChunkCullDistance * hysteresis : 0.0;
                visible = !std::isfinite(distance) || distance <= actor.ChunkCullDistance + worldRadius + margin;
            }
            _cullVisible[i] = visible ? 1 : 0;
        }
        for (auto& pair : _committed)
        {
            auto* renderer = pair.Value->Renderer.Get();
            if (!renderer)
                continue;
            bool visible = true;
            if (culling && _cullChunks > 0)
            {
                const auto& key = pair.Key;
                const int32 root = (key.Face * _cullChunks + (key.Y >> key.Level)) * _cullChunks + (key.X >> key.Level);
                if (root >= 0 && root < _cullVisible.Count())
                    visible = _cullVisible[root] != 0;
            }
            if (renderer->GetIsActive() != visible)
                renderer->SetIsActive(visible);
            if (_stopping)
                return;
        }
    }

    static bool SameBrush(const QuadSphereBrushData& x, const QuadSphereBrushData& y)
    {
        return x.Id == y.Id && x.Order == y.Order && x.Mode == y.Mode && x.Shape == y.Shape &&
            x.BiomeOverride == y.BiomeOverride && x.Position == y.Position && x.AxisX == y.AxisX &&
            x.AxisY == y.AxisY && x.AxisNormal == y.AxisNormal && x.Radius == y.Radius &&
            x.Falloff == y.Falloff && x.Depth == y.Depth && x.FlattenHeight == y.FlattenHeight;
    }

    static bool SameBrushes(const QuadSphereTerrainSnapshot& a, const QuadSphereTerrainSnapshot& b)
    {
        if (a.Brushes.Count() != b.Brushes.Count())
            return false;
        if (!a.Brushes.IsEmpty() && a.PlanetTransform != b.PlanetTransform)
            return false;
        for (int32 i = 0; i < a.Brushes.Count(); i++)
        {
            if (!SameBrush(a.Brushes[i], b.Brushes[i]))
                return false;
        }
        return true;
    }

    static void GetChangedBrushInfluences(const QuadSphereTerrainSnapshot& oldSnapshot, const QuadSphereTerrainSnapshot& newSnapshot,
                                          Array<QuadSphereBrushInfluence>& influences)
    {
        Dictionary<Guid, const QuadSphereBrushData*> oldBrushes;
        Dictionary<Guid, const QuadSphereBrushData*> newBrushes;
        for (const auto& brush : oldSnapshot.Brushes)
            oldBrushes.Add(brush.Id, &brush);
        for (const auto& brush : newSnapshot.Brushes)
            newBrushes.Add(brush.Id, &brush);
        const bool movedPlanet = oldSnapshot.PlanetTransform != newSnapshot.PlanetTransform;
        for (const auto& brush : oldSnapshot.Brushes)
        {
            const QuadSphereBrushData* updated = nullptr;
            if (movedPlanet || !newBrushes.TryGet(brush.Id, updated) || !SameBrush(brush, *updated))
                influences.Add({ &brush, &oldSnapshot.PlanetTransform });
        }
        for (const auto& brush : newSnapshot.Brushes)
        {
            const QuadSphereBrushData* previous = nullptr;
            if (movedPlanet || !oldBrushes.TryGet(brush.Id, previous) || !SameBrush(brush, *previous))
                influences.Add({ &brush, &newSnapshot.PlanetTransform });
        }
    }

    static bool BrushInfluencesPatch(const QuadSpherePatchKey& key, const QuadSphereTerrainSnapshot& snapshot,
                                     const Array<QuadSphereBrushInfluence>& influences)
    {
        const int32 divisions = snapshot.ChunksPerPatch << key.Level;
        const double u = -1.0 + 2.0 * (key.X + 0.5) / divisions;
        const double v = -1.0 + 2.0 * (key.Y + 0.5) / divisions;
        const Vector3 center(QuadSphereTerrainGeometry::Direction(key.Face, u, v) * snapshot.Radius);
        const double localRadius = snapshot.BaseElevationBound + 0.01 +
            (snapshot.Radius + snapshot.BaseElevationBound + 0.01) * (std::sqrt(2.0) / divisions + 0.00021);
        for (const auto& influence : influences)
        {
            const auto& brush = *influence.Brush;
            const Transform& transform = *influence.PlanetTransform;
            const double scale = Math::Max(std::abs((double)transform.Scale.X),
                Math::Max(std::abs((double)transform.Scale.Y), std::abs((double)transform.Scale.Z)));
            const Vector3 worldCenter = transform.LocalToWorld(center);
            const double distance = (worldCenter - brush.Position).Length();
            const double brushRadius = brush.Radius * (brush.Shape == QuadSphereBrushShape::Square ? std::sqrt(3.0) : 1.0);
            const double padding = (worldCenter.Length() + brush.Position.Length() + localRadius * scale + 1.0) * 0.00001;
            const double bound = localRadius * scale + brushRadius + padding;
            if (!std::isfinite(distance) || !std::isfinite(bound) || distance <= bound)
                return true;
        }
        return false;
    }

    void UpdateBrushes(QuadSphereActor& actor)
    {
        QuadSphereTerrainSnapshot observed;
        observed.Radius = _snapshot->Radius;
        observed.BaseElevationBound = _snapshot->BaseElevationBound;
        observed.SimpleSphere = _snapshot->SimpleSphere;
        if (!QuadSphereTerrainGeometry::CollectBrushes(actor, _spawnedPrefabRoots, observed, &_invalidBrushes) ||
            SameBrushes(*_snapshot, observed) || (_failedBrushSnapshot && SameBrushes(*_failedBrushSnapshot, observed)))
            return;

        auto candidate = std::make_shared<QuadSphereTerrainSnapshot>(*_snapshot);
        candidate->Brushes = MoveTemp(observed.Brushes);
        candidate->PlanetTransform = observed.PlanetTransform;
        if (!QuadSphereTerrainGeometry::ValidateBrushDisplacement(*candidate))
        {
            _failedBrushSnapshot = candidate;
            return;
        }

        Array<QuadSphereBrushInfluence> influences;
        GetChangedBrushInfluences(*_snapshot, *candidate, influences);
        Array<QuadSpherePatchKey> pending;
        _desired.Clear();
        for (const auto& pair : _committed)
        {
            _desired.Add(pair.Key);
            if (!IsUsable(*pair.Value, _committedCollision) || BrushInfluencesPatch(pair.Key, *_snapshot, influences))
                pending.Add(pair.Key);
        }
        if (pending.IsEmpty())
        {
            _snapshot = candidate;
            InitializeCullBounds(*_snapshot);
            _failedBrushSnapshot.reset();
            _desired.Clear();
            UpdatePatchVisibility(actor);
            return;
        }
        const int32 workerLimit = actor.GenerationWorkers <= 0 ?
            Math::Clamp((int32)Platform::GetCPUInfo().LogicalProcessorCount - 1, 1, 8) :
            Math::Clamp(actor.GenerationWorkers, 1, 32);
        if (!_generation.Begin(candidate, MoveTemp(pending), workerLimit))
        {
            _failedBrushSnapshot = candidate;
            _desired.Clear();
            return;
        }
        _candidate = candidate;
        _candidateCollision = _committedCollision;
        _replaceAll = false;
        _brushUpdate = true;
        _inFlight = true;
        _generating = true;
    }

    static bool IsUsable(const QuadSpherePatchResources& patch, bool collision)
    {
        return patch.Renderer.Get() && patch.MeshModel.Get() &&
            (!collision || (patch.Collider.Get() && patch.Collision.Get() && patch.CollisionAssigned));
    }

    static void SetShadows(PatchMap& patches, bool castShadows)
    {
        for (auto& pair : patches)
        {
            auto* renderer = pair.Value->Renderer.Get();
            if (!renderer)
                continue;
            Array<ModelInstanceEntry> entries = renderer->Entries;
            if (entries.IsEmpty())
                entries.Resize(1);
            for (auto& entry : entries)
                entry.ShadowsMode = castShadows ? ShadowsCastingMode::All : ShadowsCastingMode::None;
            renderer->SetEntries(entries);
        }
    }

    bool StartLod(QuadSphereActor& actor, const std::shared_ptr<const QuadSphereTerrainSnapshot>& snapshot, bool replaceAll)
    {
        if (!snapshot || _inFlight || !std::isfinite(actor.LodDistance))
        {
            LOG(Error, "Cannot update quad sphere LOD with invalid settings or while generation is active.");
            return false;
        }
        const Transform& transform = actor.GetTransform();
        if (transform.Translation.IsNanOrInfinity() || transform.Orientation.IsNanOrInfinity() ||
            transform.Scale.IsNanOrInfinity() || transform.Scale.X == 0.0f || transform.Scale.Y == 0.0f || transform.Scale.Z == 0.0f)
        {
            LOG(Error, "Cannot update quad sphere LOD with an invalid transform.");
            return false;
        }

        Vector3 position = Vector3::Zero;
        const bool hasTarget = TryGetTargetPosition(actor, position);
        Vector3 target = Vector3::Zero;
        if (hasTarget)
        {
            if (position.IsNanOrInfinity())
            {
                LOG(Error, "Cannot update quad sphere LOD with a non-finite target position.");
                return false;
            }
            target = transform.WorldToLocal(position);
            if (target.IsNanOrInfinity())
            {
                LOG(Error, "Cannot update quad sphere LOD with a non-finite local target position.");
                return false;
            }
        }

        Array<QuadSpherePatchKey> selected;
        for (int32 face = 0; face < 6; face++)
            for (int32 y = 0; y < snapshot->ChunksPerPatch; y++)
                for (int32 x = 0; x < snapshot->ChunksPerPatch; x++)
                    QuadSphereTerrainGeometry::SelectPatches(selected, { face, 0, x, y }, target, hasTarget,
                                                             actor.MaxLod, actor.LodDistance, *snapshot);

        const bool collision = replaceAll ? Engine::IsPlayMode() && actor.GenerateCollision : _committedCollision;
        Array<QuadSpherePatchKey> pending;
        _desired.Clear();
        for (const auto& key : selected)
        {
            _desired.Add(key);
            QuadSpherePatchResources* patch = nullptr;
            if (replaceAll || !_committed.TryGet(key, patch) || !IsUsable(*patch, collision))
                pending.Add(key);
        }
        if (!replaceAll && pending.IsEmpty() && _desired.Count() == _committed.Count())
        {
            _desired.Clear();
            return true;
        }

        const int32 workerLimit = actor.GenerationWorkers <= 0 ?
            Math::Clamp((int32)Platform::GetCPUInfo().LogicalProcessorCount - 1, 1, 8) :
            Math::Clamp(actor.GenerationWorkers, 1, 32);
        if (!_generation.Begin(snapshot, MoveTemp(pending), workerLimit))
        {
            _desired.Clear();
            return false;
        }
        _candidate = snapshot;
        _candidateCollision = collision;
        _replaceAll = replaceAll;
        _inFlight = true;
        _generating = true;
        return true;
    }

    void Commit(QuadSphereActor& actor)
    {
        for (const auto& pair : _staged)
        {
            if (!IsUsable(*pair.Value, _candidateCollision))
            {
                LOG(Error, "Cannot commit an unavailable quad sphere patch; keeping the previous terrain.");
                Fail();
                return;
            }
        }
        for (auto& pair : _staged)
        {
            pair.Value->Renderer->SetIsActive(true);
            if (_stopping)
                return;
            if (pair.Value->Collider.Get())
                pair.Value->Collider->SetIsActive(true);
            if (_stopping)
                return;
        }

        Array<QuadSpherePatchKey> remove;
        for (const auto& pair : _committed)
            if (_replaceAll || !_desired.Contains(pair.Key) || _staged.ContainsKey(pair.Key))
                remove.Add(pair.Key);
        if (_replaceAll)
            ClearFoliage();
        else
        {
            Array<QuadSpherePatchKey> changed(remove);
            for (const auto& pair : _staged)
                changed.Add(pair.Key);
            InvalidateFoliageForPatches(changed);
        }
        if (_stopping)
            return;
        for (const auto& key : remove)
        {
            auto* patch = _committed[key];
            QuadSpherePatchInstallation::Destroy(*patch);
            Delete(patch);
            _committed.Remove(key);
            if (_stopping)
                return;
        }
        for (const auto& pair : _staged)
            _committed.Add(pair.Key, pair.Value);
        _staged.Clear();
        if (_snapshot != _candidate)
            InitializeCullBounds(*_candidate);
        _snapshot = _candidate;
        _committedCollision = _candidateCollision;
        if (_replaceAll)
        {
            _prefabConfigs = MoveTemp(_pendingPrefabConfigs);
            _foliageConfigs = MoveTemp(_pendingFoliageConfigs);
            _foliageDivisions = _pendingFoliageDivisions;
            _foliageSurfaceErrorLogged = false;
            _failedFoliageAssets.Clear();
            _invalidFoliageColliders.Clear();
            if (_prefabConfigs.IsEmpty())
                _prefabSnapshot.reset();
            else
            {
                auto prefabSnapshot = std::make_shared<QuadSphereTerrainSnapshot>(*_snapshot);
                prefabSnapshot->Brushes.Clear();
                _prefabSnapshot = prefabSnapshot;
            }
            _failedPrefabAssets.Clear();
            for (int32 i = 0; i < _prefabConfigs.Count(); i++)
                if (_prefabConfigs[i].Prefab.Get() && _prefabConfigs[i].Density != 0.0f && _prefabConfigs[i].Probability != 0.0f &&
                    !QuadSphereTerrainGeometry::ValidPrefabConfig(_prefabConfigs[i]))
                    LOG(Warning, "Quad sphere prefab entry {0} has invalid placement settings and will be ignored.", i);
            for (int32 i = 0; i < _foliageConfigs.Count(); i++)
                if (_foliageConfigs[i].Model.Get() && _foliageConfigs[i].Density != 0.0f && _foliageConfigs[i].Probability != 0.0f &&
                    !QuadSphereTerrainGeometry::ValidFoliageConfig(_foliageConfigs[i]))
                    LOG(Warning, "Quad sphere foliage entry {0} has invalid placement settings and will be ignored.", i);
        }
        if (_brushUpdate)
            _failedBrushSnapshot.reset();
        _candidate.reset();
        _desired.Clear();
        _inFlight = false;
        _brushUpdate = false;
        _elapsed = 0.0f;
        UpdatePatchVisibility(actor);
    }

    void Advance(QuadSphereActor& actor)
    {
        const double budget = std::isfinite(actor.PatchInstallBudgetMs) ? Math::Clamp(actor.PatchInstallBudgetMs, 0.1f, 100.0f) : 3.0;
        const double start = Platform::GetTimeSeconds();
        while (_generating)
        {
            QuadSpherePatchGeometry geometry;
            const auto result = _generation.TakeNext(geometry);
            if (result == QuadSphereGenerationResult::Pending)
                break;
            if (result == QuadSphereGenerationResult::Failed)
            {
                Fail();
                return;
            }
            if (result == QuadSphereGenerationResult::Complete)
            {
                _generating = false;
                break;
            }
            auto* patch = New<QuadSpherePatchResources>();
            if (!QuadSpherePatchInstallation::Create(actor, geometry, _candidateCollision, _candidate->RetainSurfaceVertices, *patch))
            {
                QuadSpherePatchInstallation::Destroy(*patch);
                Delete(patch);
                Fail();
                return;
            }
            if (_stopping || (_candidateCollision && !QuadSpherePatchInstallation::StartCollision(*patch, MoveTemp(geometry))))
            {
                QuadSpherePatchInstallation::Destroy(*patch);
                Delete(patch);
                if (!_stopping)
                    Fail();
                return;
            }
            _staged.Add(patch->Key, patch);
            if ((Platform::GetTimeSeconds() - start) * 1000.0 >= budget)
                break;
        }
        if (_generating)
            return;

        bool waiting = false;
        for (auto& pair : _staged)
        {
            auto& patch = *pair.Value;
            if (patch.CollisionAssigned)
                continue;
            const auto state = QuadSpherePatchInstallation::FinishCollision(patch);
            if (state == QuadSphereCookState::Queued || state == QuadSphereCookState::Running)
                waiting = true;
            else if (state != QuadSphereCookState::Succeeded)
            {
                Fail();
                return;
            }
        }
        if (!waiting)
            Commit(actor);
    }

public:
    // Prefab callbacks can end play while an update or rebuild is still on the stack.
    void Retain()
    {
        _activeCalls++;
    }

    void Release()
    {
        _activeCalls--;
        if (_stopping && _activeCalls == 0)
        {
            Clear();
            Delete(this);
        }
    }

    void Stop()
    {
        _stopping = true;
        if (_activeCalls == 0)
        {
            Clear();
            Delete(this);
        }
    }

    void Clear()
    {
        Abort();
        ClearPrefabs();
        ClearFoliage();
        ClearPatches(_committed);
        _snapshot.reset();
        _prefabSnapshot.reset();
        _prefabConfigs.Clear();
        _pendingPrefabConfigs.Clear();
        _foliageConfigs.Clear();
        _pendingFoliageConfigs.Clear();
        _failedFoliageAssets.Clear();
        _invalidFoliageColliders.Clear();
        _failedBrushSnapshot.reset();
        _cullCenters.Clear();
        _cullVisible.Clear();
        _cullChunks = 0;
        _committedCollision = false;
    }

    static bool HasCollision(const QuadSpherePatchResources& patch)
    {
        auto* collider = patch.Collider.Get();
        auto* collision = patch.Collision.Get();
        return patch.CollisionAssigned && collider && collider->IsActiveInHierarchy() && collision &&
            collision->IsLoaded() && collider->CollisionData.Get() == collision;
    }

    bool TrySampleCommittedSurface(int32 face, double u, double v, const Double3& direction, Double3& surface) const
    {
        if (!_snapshot || face < 0 || face >= 6 || !std::isfinite(u) || !std::isfinite(v) ||
            u < -1.0 || u > 1.0 || v < -1.0 || v > 1.0 || direction.IsNanOrInfinity())
            return false;
        for (int32 level = 0; level <= 10; level++)
        {
            const int32 divisions = _snapshot->ChunksPerPatch << level;
            const double patchX = (u + 1.0) * 0.5 * divisions;
            const double patchY = (v + 1.0) * 0.5 * divisions;
            const int32 x = Math::Clamp((int32)patchX, 0, divisions - 1);
            const int32 y = Math::Clamp((int32)patchY, 0, divisions - 1);
            QuadSpherePatchResources* patch = nullptr;
            if (_committed.TryGet(QuadSpherePatchKey{ face, level, x, y }, patch))
                return QuadSphereTerrainGeometry::TrySamplePatchSurface(*patch, _snapshot->Resolution,
                    patchX - x, patchY - y, direction, surface);
        }
        return false;
    }

    bool IsTerrainReady(const QuadSphereActor& actor) const
    {
        if (!Engine::IsPlayMode() || !actor.IsActiveInHierarchy() || !actor.GenerateCollision ||
            !_snapshot || !_committedCollision || _committed.IsEmpty())
            return false;
        for (const auto& pair : _committed)
            if (!HasCollision(*pair.Value))
                return false;
        return true;
    }

    bool IsGroundReady(const QuadSphereActor& actor, const Vector3& worldPosition) const
    {
        if (!Engine::IsPlayMode() || !actor.IsActiveInHierarchy() || !actor.GenerateCollision ||
            !_snapshot || !_committedCollision || worldPosition.IsNanOrInfinity())
            return false;
        const Transform& transform = actor.GetTransform();
        if (transform.Translation.IsNanOrInfinity() || transform.Orientation.IsNanOrInfinity() ||
            transform.Scale.IsNanOrInfinity() || transform.Scale.X == 0.0f || transform.Scale.Y == 0.0f || transform.Scale.Z == 0.0f)
            return false;
        const Vector3 local = transform.WorldToLocal(worldPosition);
        const double ax = std::abs((double)local.X), ay = std::abs((double)local.Y), az = std::abs((double)local.Z);
        if (!std::isfinite(ax) || !std::isfinite(ay) || !std::isfinite(az) || Math::Max(ax, Math::Max(ay, az)) < 0.000001)
            return false;

        int32 face;
        double u, v;
        if (ax >= ay && ax >= az)
        {
            face = local.X >= 0 ? 0 : 1;
            u = (face == 0 ? -local.Z : local.Z) / ax;
            v = local.Y / ax;
        }
        else if (ay >= az)
        {
            face = local.Y >= 0 ? 2 : 3;
            u = local.X / ay;
            v = (face == 2 ? -local.Z : local.Z) / ay;
        }
        else
        {
            face = local.Z >= 0 ? 4 : 5;
            u = (face == 4 ? local.X : -local.X) / az;
            v = local.Y / az;
        }
        for (int32 level = 0; level <= 10; level++)
        {
            const int32 divisions = _snapshot->ChunksPerPatch << level;
            const int32 x = Math::Clamp((int32)((u + 1.0) * 0.5 * divisions), 0, divisions - 1);
            const int32 y = Math::Clamp((int32)((v + 1.0) * 0.5 * divisions), 0, divisions - 1);
            QuadSpherePatchResources* patch = nullptr;
            if (_committed.TryGet(QuadSpherePatchKey{ face, level, x, y }, patch) && HasCollision(*patch))
                return true;
        }
        return false;
    }

    template<typename Entry, typename Config>
    static bool LoadPlacementAssets(const Array<AssetReference<JsonAsset>>& assets, Array<Config>& configs, const Char* kind)
    {
        for (const auto& reference : assets)
        {
            JsonAsset* asset = reference.Get();
            if (!asset)
                continue;
            if (asset->WaitForLoaded())
            {
                LOG(Error, "Failed to load quad sphere {0} entry asset {1}.", kind, asset->GetID());
                return false;
            }
            ScopeLock lock(asset->Locker);
            const auto* entry = asset->GetInstance<Entry>();
            if (!entry)
            {
                LOG(Error, "Quad sphere {0} entry asset {1} has the wrong data type or could not be deserialized.", kind, asset->GetID());
                return false;
            }
            configs.Add(entry->Settings);
        }
        return true;
    }

    void Rebuild(QuadSphereActor& actor)
    {
        if (_stopping)
            return;
        if (_activeCalls > 1 || _prefabOperationDepth != 0)
        {
            _rebuildRequested = true;
            return;
        }
        _rebuildRequested = false;
        _initialRebuild = false;
        _lastSimpleSphere = actor.SimpleSphere;
        _lastGenerateCollision = actor.GenerateCollision;
        Array<QuadSpherePrefabConfig> prefabConfigs;
        Array<QuadSphereFoliageConfig> foliageConfigs;
        if (!LoadPlacementAssets<QuadSpherePrefabAsset>(actor.PrefabAssets, prefabConfigs, TEXT("prefab")) ||
            !LoadPlacementAssets<QuadSphereFoliageAsset>(actor.FoliageAssets, foliageConfigs, TEXT("foliage")))
            return;
        Abort();
        ClearPrefabs();
        if (_stopping)
            return;
        _failedBrushSnapshot.reset();
        _brushCheckElapsed = 0.0f;
        auto snapshot = std::make_shared<QuadSphereTerrainSnapshot>();
        const Array<Guid> spawnedPrefabRoots;
        if (!QuadSphereTerrainGeometry::CreateSnapshot(actor, true, spawnedPrefabRoots, *snapshot, &_invalidBrushes))
            return;
        _pendingPrefabConfigs = MoveTemp(prefabConfigs);
        _pendingFoliageConfigs = MoveTemp(foliageConfigs);
        _pendingFoliageDivisions = Math::Clamp(actor.FoliageCellsPerFace, 1, 1024);
        StartLod(actor, snapshot, true);
    }

    void Update(QuadSphereActor& actor)
    {
        UpdatePrefabDiagnostics(actor);
        if (_initialRebuild || _rebuildRequested || (actor.IsActiveInHierarchy() &&
            (_lastSimpleSphere != actor.SimpleSphere || _lastGenerateCollision != actor.GenerateCollision)))
        {
            Rebuild(actor);
            return;
        }
        if (!actor.IsActiveInHierarchy())
            return;
        if (_castShadows != actor.CastShadows)
        {
            _castShadows = actor.CastShadows;
            SetShadows(_committed, _castShadows);
            SetShadows(_staged, _castShadows);
        }
        if (_snapshot)
            UpdatePatchVisibility(actor);
        if (_stopping)
            return;
        if (!Engine::IsPlayMode())
        {
            if (!actor.PreviewFoliageInEditor)
                ClearFoliage();
            else
                RecycleFoliageColliders();
            if (_stopping || _rebuildRequested)
                return;
        }
        if (_inFlight)
        {
            CleanupPrefabDistances(actor);
            if (_stopping)
                return;
            Advance(actor);
            return;
        }
        if (!_snapshot)
            return;
        const bool prefabsPending = UpdatePrefabs(actor);
        if (_stopping || _rebuildRequested)
            return;
        if (!_snapshot->SimpleSphere)
        {
            _brushCheckElapsed += Time::GetUnscaledDeltaTime();
            if (_brushCheckElapsed >= 0.25f || (!prefabsPending && _prefabBrushesDirty))
            {
                _prefabBrushesDirty = false;
                _brushCheckElapsed = 0.0f;
                UpdateBrushes(actor);
                if (_inFlight)
                    return;
            }
        }
        if (!prefabsPending)
        {
            UpdateFoliage(actor);
            if (_stopping || _rebuildRequested)
                return;
        }
        _elapsed += Engine::IsPlayMode() ? Time::GetDeltaTime() : Time::GetUnscaledDeltaTime();
        const float interval = std::isfinite(actor.LodUpdateInterval) ? Math::Max(0.05f, actor.LodUpdateInterval) : 0.5f;
        if (_elapsed >= interval)
        {
            _elapsed = 0.0f;
            StartLod(actor, _snapshot, false);
        }
    }
};

QuadSphereActor::QuadSphereActor(const SpawnParams& params)
    : Actor(params)
{
}

QuadSphereActor::~QuadSphereActor()
{
    if (_runtime)
    {
        Scripting::Update.Unbind<QuadSphereActor, &QuadSphereActor::UpdateTerrain>(this);
        auto* runtime = _runtime;
        _runtime = nullptr;
        runtime->Stop();
    }
}

bool QuadSphereActor::GetCoordinates(const Vector3& worldPosition, float& latitude, float& longitude, float& altitude) const
{
    latitude = longitude = altitude = 0.0f;
    if (!std::isfinite(Radius) || Radius <= 0.0 || _transform.Translation.IsNanOrInfinity() ||
        _transform.Orientation.IsNanOrInfinity() || _transform.Scale.IsNanOrInfinity() ||
        _transform.Scale.X == 0.0f || _transform.Scale.Y == 0.0f || _transform.Scale.Z == 0.0f)
    {
        LOG(Error, "Quad sphere coordinates require a positive finite radius and a finite transform with nonzero scale.");
        return false;
    }
    if (worldPosition.IsNanOrInfinity())
    {
        LOG(Error, "Quad sphere coordinates require a finite world position.");
        return false;
    }

    const Double3 local(_transform.WorldToLocal(worldPosition));
    const double distance = local.Length();
    if (!std::isfinite(distance) || distance == 0.0)
    {
        LOG(Error, "Quad sphere coordinates require a finite, nonzero distance from the planet center.");
        return false;
    }
    const double height = distance - Radius;
    if (!std::isfinite(height) || height > MAX_float || height < -MAX_float)
    {
        LOG(Error, "Quad sphere altitude is outside the float range.");
        return false;
    }
    const double horizontal = std::sqrt(local.X * local.X + local.Z * local.Z);
    constexpr double radiansToDegrees = 180.0 / 3.14159265358979323846;
    latitude = (float)(std::atan2(local.Y, horizontal) * radiansToDegrees);
    longitude = horizontal == 0.0 ? 0.0f : (float)(std::atan2(local.Z, local.X) * radiansToDegrees);
    altitude = (float)height;
    return true;
}

void QuadSphereActor::OnBeginPlay()
{
    Actor::OnBeginPlay();
    _runtime = New<QuadSphereRuntime>();
    Scripting::Update.Bind<QuadSphereActor, &QuadSphereActor::UpdateTerrain>(this);
}

void QuadSphereActor::OnEndPlay()
{
    Scripting::Update.Unbind<QuadSphereActor, &QuadSphereActor::UpdateTerrain>(this);
    if (_runtime)
    {
        auto* runtime = _runtime;
        _runtime = nullptr;
        runtime->Stop();
    }
    Actor::OnEndPlay();
}

void QuadSphereActor::RebuildTerrain()
{
    if (!_runtime)
    {
        LOG(Warning, "Add the quad sphere actor to an active scene before rebuilding the terrain.");
        return;
    }
    auto* runtime = _runtime;
    runtime->Retain();
    runtime->Rebuild(*this);
    runtime->Release();
}

bool QuadSphereActor::GetIsTerrainReady() const
{
    return _runtime && _runtime->IsTerrainReady(*this);
}

bool QuadSphereActor::IsGroundReady(const Vector3& worldPosition) const
{
    return _runtime && _runtime->IsGroundReady(*this, worldPosition);
}

void QuadSphereActor::UpdateTerrain()
{
    if (_runtime)
    {
        auto* runtime = _runtime;
        runtime->Retain();
        runtime->Update(*this);
        runtime->Release();
    }
}

QuadSphereBrush::QuadSphereBrush(const SpawnParams& params)
    : Actor(params)
{
}

bool QuadSphereBrush::TargetsActor(const QuadSphereActor* actor) const
{
    if (!actor)
        return false;
    for (const auto& target : Targets)
    {
        if (target.Get() == actor)
            return true;
    }
    return false;
}

void QuadSphereBrush::GetSquareAxes(const Vector3& position, const Vector3& planetCenter, const Quaternion& rotation,
                                    Vector3& normal, Vector3& axisX, Vector3& axisY)
{
    normal = position - planetCenter;
    normal = normal.LengthSquared() > 0.000000000001 ? Vector3::Normalize(normal) : Vector3::UnitY;
    axisX = Vector3::Transform(Vector3::UnitX, rotation);
    axisX -= normal * Vector3::Dot(axisX, normal);
    if (axisX.LengthSquared() < 0.000000000001)
    {
        axisX = Vector3::Transform(Vector3::UnitZ, rotation);
        axisX -= normal * Vector3::Dot(axisX, normal);
    }
    axisX.Normalize();
    axisY = Vector3::Cross(normal, axisX);
}

void QuadSphereBrush::Serialize(SerializeStream& stream, const void* otherObj)
{
    Actor::Serialize(stream, otherObj);
    SERIALIZE_GET_OTHER_OBJ(QuadSphereBrush);
    SERIALIZE(Targets);
    SERIALIZE(Mode);
    SERIALIZE(BiomeOverride);
    SERIALIZE(Shape);
    SERIALIZE(Radius);
    SERIALIZE(Falloff);
    SERIALIZE(Depth);
    SERIALIZE(Order);
}

void QuadSphereBrush::Deserialize(DeserializeStream& stream, ISerializeModifier* modifier)
{
    Actor::Deserialize(stream, modifier);
    DESERIALIZE(Targets);
    DESERIALIZE(Mode);
    DESERIALIZE(BiomeOverride);
    DESERIALIZE(Shape);
    DESERIALIZE(Radius);
    DESERIALIZE(Falloff);
    DESERIALIZE(Depth);
    DESERIALIZE(Order);
}

#if USE_EDITOR
#include "Engine/Debug/DebugDraw.h"

void QuadSphereBrush::OnDebugDrawSelected()
{
    Actor::OnDebugDrawSelected();
    if (!std::isfinite(Radius) || Radius <= 0.0f)
        return;
    const Vector3 position = GetPosition();
    if (Shape == QuadSphereBrushShape::Circle)
    {
        DEBUG_DRAW_WIRE_SPHERE(BoundingSphere(position, Radius), Color::Yellow, 0, true);
    }
    else if (Shape == QuadSphereBrushShape::Square)
    {
        for (const auto& target : Targets)
        {
            if (!target.Get())
                continue;
            Vector3 normal, x, y;
            GetSquareAxes(position, target.Get()->GetPosition(), GetOrientation(), normal, x, y);
            x *= Radius;
            y *= Radius;
            normal *= Radius;
            for (int32 side = -1; side <= 1; side += 2)
            {
                const Vector3 center = position + normal * (Real)side;
                DEBUG_DRAW_LINE(center - x - y, center + x - y, Color::Yellow, 0, true);
                DEBUG_DRAW_LINE(center + x - y, center + x + y, Color::Yellow, 0, true);
                DEBUG_DRAW_LINE(center + x + y, center - x + y, Color::Yellow, 0, true);
                DEBUG_DRAW_LINE(center - x + y, center - x - y, Color::Yellow, 0, true);
            }
            DEBUG_DRAW_LINE(position - x - y - normal, position - x - y + normal, Color::Yellow, 0, true);
            DEBUG_DRAW_LINE(position + x - y - normal, position + x - y + normal, Color::Yellow, 0, true);
            DEBUG_DRAW_LINE(position + x + y - normal, position + x + y + normal, Color::Yellow, 0, true);
            DEBUG_DRAW_LINE(position - x + y - normal, position - x + y + normal, Color::Yellow, 0, true);
        }
    }
}
#endif

// Copyright (c) Wojciech Figat. All rights reserved.

namespace FlaxEngine
{
    public partial class DirectionalLight
    {
        /// <summary>Gets whether the first cascade spacing is visible.</summary>
        protected bool ShowCascade1 => CascadeCount >= 1 && PartitionMode == PartitionMode.Manual;
        /// <summary>Gets whether the second cascade spacing is visible.</summary>
        protected bool ShowCascade2 => CascadeCount >= 2 && PartitionMode == PartitionMode.Manual;
        /// <summary>Gets whether the third cascade spacing is visible.</summary>
        protected bool ShowCascade3 => CascadeCount >= 3 && PartitionMode == PartitionMode.Manual;
        /// <summary>Gets whether the fourth cascade spacing is visible.</summary>
        protected bool ShowCascade4 => CascadeCount >= 4 && PartitionMode == PartitionMode.Manual;
    }
}

namespace DevOpsManagement.Tools
{
    using System;
    using System.Collections.Generic;

    public sealed class AzIdCreator
    {
        private static int currentAzId;
        
            private static readonly Lazy<AzIdCreator>
                lazy =
                new Lazy<AzIdCreator>
                    (() => new AzIdCreator());

            public static AzIdCreator Instance { get { return lazy.Value; } }

        public int EnvironmentSeed
        {
            set
            {
                currentAzId = value;
            }
        }
        public int NextAzId()
        {
            
            return ++currentAzId;            
        }

        private AzIdCreator()
        {
        }
    }
    
}

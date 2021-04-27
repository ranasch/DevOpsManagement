namespace DevOpsManagement.Tools
{
    using System;
    using System.Collections.Generic;

    public sealed class AzIdCreator
    {
        private static Dictionary<string, int> currentAzIds;
        
            private static readonly Lazy<AzIdCreator>
                lazy =
                new Lazy<AzIdCreator>
                    (() => new AzIdCreator());

            public static AzIdCreator Instance { get { return lazy.Value; } }

        public Dictionary<string, int> EnvironmentSeed
        {
            set
            {
                currentAzIds = value;
            }
        }
        public int NextAzId(string environment)
        {
            
            return ++currentAzIds[environment];            
        }

            private AzIdCreator()
            {
            }
        }
    
}

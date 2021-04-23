namespace DevOpsManagement.Tools
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    public class ProjectNameComparer : IEqualityComparer<string>
    {
        public bool Equals([AllowNull] string existing, [AllowNull] string proposed)
        {
            if (existing.Contains(proposed))
            {
                return true;
            }
            return false;
        }

        public int GetHashCode(string name)
        {
            return name.GetHashCode();
        }
    }
}

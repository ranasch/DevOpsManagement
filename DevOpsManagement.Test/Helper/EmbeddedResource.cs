namespace DevOpsManagement.Test.Helper;
using System;
using System.IO;

public static class EmbeddedResource
{
    public static string GetResource(Type typeFromResourceAssembly, string resourceName)
    {
        var embeddedResourceName = $"{typeFromResourceAssembly.Assembly.GetName().Name}.{resourceName}";
        string content = string.Empty;
        var handle = typeFromResourceAssembly.Assembly.GetManifestResourceStream(embeddedResourceName);
        if (handle is null)
            return "";
        using (Stream stream = handle)
        using (StreamReader reader = new(stream))
        {
            content = reader.ReadToEnd();
        }
        return content;
    }

}

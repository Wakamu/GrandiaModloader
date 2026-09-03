using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace GrandiaModloader;

/// <summary>
/// Reads <c>[Mod(name, version, Description = ...)]</c> from a DLL's metadata
/// without loading Grandia.Sdk or executing the assembly.
/// </summary>
internal static class ModMetadataReader
{
    public readonly record struct Info(string Name, string Version, string Description);

    public static Info Read(string dllPath)
    {
        var fallback = Path.GetFileNameWithoutExtension(dllPath);
        try
        {
            using var fs = File.OpenRead(dllPath);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata)
            {
                return new Info(fallback, "", "");
            }

            var md = pe.GetMetadataReader();
            foreach (var handle in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(handle);
                foreach (var attrHandle in type.GetCustomAttributes())
                {
                    if (TryReadMod(md, md.GetCustomAttribute(attrHandle), fallback, out var info))
                    {
                        return info;
                    }
                }
            }

            return ReadAssemblyInfo(md, fallback);
        }
        catch
        {
            return new Info(fallback, "", "");
        }
    }

    private static bool TryReadMod(MetadataReader md, CustomAttribute attr, string fallback,
        out Info info)
    {
        info = default;
        if (!IsNamed(md, attr.Constructor, "ModAttribute", "Grandia.Sdk"))
        {
            return false;
        }

        string? name = null;
        var version = "";
        var description = "";
        try
        {
            var blob = md.GetBlobReader(attr.Value);
            if (blob.ReadUInt16() != 1)
            {
                return false;
            }

            var strings = CtorStringCount(md, attr.Constructor);
            if (strings >= 1)
            {
                name = blob.ReadSerializedString();
            }

            if (strings >= 2)
            {
                version = blob.ReadSerializedString() ?? "";
            }

            if (blob.RemainingBytes >= 2)
            {
                var named = blob.ReadUInt16();
                for (var i = 0; i < named && blob.RemainingBytes > 0; i++)
                {
                    blob.ReadByte();
                    var type = (SerializationTypeCode)blob.ReadByte();
                    var prop = blob.ReadSerializedString() ?? "";
                    if (type != SerializationTypeCode.String)
                    {
                        break;
                    }

                    var value = blob.ReadSerializedString() ?? "";
                    switch (prop)
                    {
                        case "Name":
                            name = value;
                            break;
                        case "Version":
                            version = value;
                            break;
                        case "Description":
                            description = value;
                            break;
                    }
                }
            }
        }
        catch (BadImageFormatException)
        {
            return false;
        }

        info = new Info(
            string.IsNullOrWhiteSpace(name) ? fallback : name.Trim(),
            version.Trim(),
            description.Trim());
        return true;
    }

    private static Info ReadAssemblyInfo(MetadataReader md, string fallback)
    {
        string? title = null;
        string? description = null;
        var version = "";
        try
        {
            version = md.GetAssemblyDefinition().Version.ToString();
            foreach (var handle in md.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attr = md.GetCustomAttribute(handle);
                var typeName = AttributeTypeName(md, attr.Constructor);
                if (typeName is not ("AssemblyTitleAttribute" or "AssemblyDescriptionAttribute"
                    or "AssemblyInformationalVersionAttribute"))
                {
                    continue;
                }

                var blob = md.GetBlobReader(attr.Value);
                if (blob.ReadUInt16() != 1)
                {
                    continue;
                }

                var value = blob.RemainingBytes > 0 ? blob.ReadSerializedString() : null;
                if (typeName == "AssemblyTitleAttribute")
                {
                    title = value;
                }
                else if (typeName == "AssemblyDescriptionAttribute")
                {
                    description = value;
                }
                else if (!string.IsNullOrWhiteSpace(value))
                {
                    version = value;
                }
            }
        }
        catch
        {
            // Filename is enough.
        }

        return new Info(
            string.IsNullOrWhiteSpace(title) ? fallback : title.Trim(),
            version.Trim(),
            description?.Trim() ?? "");
    }

    private static int CtorStringCount(MetadataReader md, EntityHandle ctor)
    {
        BlobHandle sig = default;
        if (ctor.Kind == HandleKind.MemberReference)
        {
            sig = md.GetMemberReference((MemberReferenceHandle)ctor).Signature;
        }
        else if (ctor.Kind == HandleKind.MethodDefinition)
        {
            sig = md.GetMethodDefinition((MethodDefinitionHandle)ctor).Signature;
        }
        else
        {
            return 0;
        }

        var br = md.GetBlobReader(sig);
        br.ReadSignatureHeader();
        var count = br.ReadCompressedInteger();
        br.ReadSignatureTypeCode();
        return count;
    }

    private static bool IsNamed(MetadataReader md, EntityHandle ctor, string typeName, string ns)
    {
        var name = AttributeTypeName(md, ctor);
        if (name != typeName)
        {
            return false;
        }

        var typeNs = AttributeTypeNamespace(md, ctor);
        return string.IsNullOrEmpty(typeNs) || typeNs == ns;
    }

    private static string? AttributeTypeName(MetadataReader md, EntityHandle ctor) =>
        AttributeType(md, ctor, ns: false);

    private static string? AttributeTypeNamespace(MetadataReader md, EntityHandle ctor) =>
        AttributeType(md, ctor, ns: true);

    private static string? AttributeType(MetadataReader md, EntityHandle ctor, bool ns)
    {
        try
        {
            if (ctor.Kind == HandleKind.MemberReference)
            {
                var parent = md.GetMemberReference((MemberReferenceHandle)ctor).Parent;
                if (parent.Kind == HandleKind.TypeReference)
                {
                    var tr = md.GetTypeReference((TypeReferenceHandle)parent);
                    return ns ? md.GetString(tr.Namespace) : md.GetString(tr.Name);
                }

                if (parent.Kind == HandleKind.TypeDefinition)
                {
                    var td = md.GetTypeDefinition((TypeDefinitionHandle)parent);
                    return ns ? md.GetString(td.Namespace) : md.GetString(td.Name);
                }
            }
            else if (ctor.Kind == HandleKind.MethodDefinition)
            {
                var method = md.GetMethodDefinition((MethodDefinitionHandle)ctor);
                var td = md.GetTypeDefinition(method.GetDeclaringType());
                return ns ? md.GetString(td.Namespace) : md.GetString(td.Name);
            }
        }
        catch (BadImageFormatException)
        {
            return null;
        }

        return null;
    }
}

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: AssemblyMetadataReader <assembly-path> [--contains=<name> ...]");
    return 2;
}

string assemblyPath = Path.GetFullPath(args[0]);
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine("Assembly not found: " + assemblyPath);
    return 3;
}

using FileStream stream = File.OpenRead(assemblyPath);
using PEReader peReader = new PEReader(stream);
if (!peReader.HasMetadata)
{
    Console.Error.WriteLine("Assembly has no metadata: " + assemblyPath);
    return 4;
}

MetadataReader metadata = peReader.GetMetadataReader();
AssemblyDefinition definition = metadata.GetAssemblyDefinition();
string assemblyName = metadata.GetString(definition.Name);
Guid mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);

Console.WriteLine("assemblyName=" + assemblyName);
Console.WriteLine("assemblyVersion=" + definition.Version);
Console.WriteLine("mvid=" + mvid.ToString("D"));

foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
{
    AssemblyReference reference = metadata.GetAssemblyReference(handle);
    Console.WriteLine("reference=" + metadata.GetString(reference.Name) + "|" + reference.Version);
}

HashSet<string> symbols = new HashSet<string>(StringComparer.Ordinal);
foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
{
    TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
    string typeName = metadata.GetString(type.Name);
    string namespaceName = metadata.GetString(type.Namespace);
    string qualifiedName = namespaceName.Length == 0 ? typeName : namespaceName + "." + typeName;
    symbols.Add(qualifiedName);
    foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        symbols.Add(qualifiedName + "::" + metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name));
}

foreach (string requested in args.Skip(1).Where(value => value.StartsWith("--contains=", StringComparison.Ordinal)))
{
    string symbol = requested.Substring("--contains=".Length);
    Console.WriteLine("contains=" + symbol + "|" + symbols.Contains(symbol));
}

return 0;

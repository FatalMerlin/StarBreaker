using System.Text;

namespace StarBreaker.DataCore.TypeGenerator;

public class DataCoreTypeGenerator
{
    private readonly DataCoreDatabase Database;
    private readonly string _namespace;
    private readonly string _dataCoreClassName;

    public DataCoreTypeGenerator(DataCoreDatabase database, string @namespace = "StarBreaker.DataCoreGenerated", string dataCoreClassName = "DataCoreBinary")
    {
        Database = database;
        _namespace = @namespace;
        _dataCoreClassName = dataCoreClassName;
    }

    /// <summary>
    /// Generates a complete, buildable project with all types and infrastructure.
    /// </summary>
    public void Generate(string path, bool includeProjectFile = true)
    {
        Directory.CreateDirectory(path);

        GenerateTypes(path);
        GenerateEnums(path);
        GenerateDataCoreBinary(path);

        if (includeProjectFile)
            GenerateProjectFile(path);
    }

    private void GenerateProjectFile(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <LangVersion>latest</LangVersion>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <!-- Reference the StarBreaker.DataCore NuGet package or project -->");
        sb.AppendLine("    <!-- Option 1: NuGet package (when published) -->");
        sb.AppendLine("    <!-- <PackageReference Include=\"StarBreaker.DataCore\" Version=\"1.0.0\" /> -->");
        sb.AppendLine("    ");
        sb.AppendLine("    <!-- Option 2: Project reference (for development) -->");
        sb.AppendLine("    <!-- <ProjectReference Include=\"path/to/StarBreaker.DataCore.csproj\" /> -->");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("</Project>");

        File.WriteAllText(Path.Combine(path, $"{_namespace}.csproj"), sb.ToString());
    }

    private void GenerateDataCoreBinary(string path)
    {
        var sb = new StringBuilder();

        sb.AppendLine("using StarBreaker.Common;");
        sb.AppendLine("using StarBreaker.DataCore;");
        sb.AppendLine();
        sb.AppendLine($"namespace {_namespace};");
        sb.AppendLine();

        // Constants
        sb.AppendLine("public static class DataCoreConstants");
        sb.AppendLine("{");
        sb.AppendLine($"    public const int StructCount = {Database.StructDefinitions.Length};");
        sb.AppendLine($"    public const int EnumCount = {Database.EnumDefinitions.Length};");
        sb.AppendLine($"    public const int StructsHash = {Database.StructsHash};");
        sb.AppendLine($"    public const int EnumsHash = {Database.EnumsHash};");
        sb.AppendLine("}");
        sb.AppendLine();

        // Main DataCoreBinary class
        sb.AppendLine($"public sealed class {_dataCoreClassName}");
        sb.AppendLine("{");
        sb.AppendLine("    public DataCoreTypedReader Reader { get; }");
        sb.AppendLine();
        sb.AppendLine($"    public {_dataCoreClassName}(DataCoreDatabase database)");
        sb.AppendLine("    {");
        sb.AppendLine("        Reader = new DataCoreTypedReader(database, ReadFromRecord);");
        sb.AppendLine("        Reader.ValidateSchema(DataCoreConstants.StructCount, DataCoreConstants.EnumCount, DataCoreConstants.StructsHash, DataCoreConstants.EnumsHash);");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ReadFromRecord switch statement
        sb.AppendLine("    private IDataCoreTypedReadable? ReadFromRecord(int structIndex, int instanceIndex)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (structIndex == -1 || instanceIndex == -1)");
        sb.AppendLine("            return null;");
        sb.AppendLine();
        sb.AppendLine("        return structIndex switch");
        sb.AppendLine("        {");

        for (var i = 0; i < Database.StructDefinitions.Length; i++)
        {
            var structDefinition = Database.StructDefinitions[i];
            sb.AppendLine($"            {i} => Reader.GetOrReadInstance<{structDefinition.GetName(Database)}>(structIndex, instanceIndex),");
        }

        sb.AppendLine("            _ => throw new NotImplementedException($\"Unknown struct index: {structIndex}\")");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(path, $"{_dataCoreClassName}.cs"), sb.ToString());
    }

    private void GenerateEnums(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "Enums"));
        foreach (var enumDefinition in Database.EnumDefinitions)
        {
            var fileName = enumDefinition.GetName(Database) + ".cs";
            var sb = new StringBuilder();

            sb.AppendLine($"namespace {_namespace};");
            sb.AppendLine();
            sb.AppendLine($"public enum {enumDefinition.GetName(Database)} : int");
            sb.AppendLine("{");
            sb.AppendLine("    __Unknown = -1,");

            for (var i = 0; i < enumDefinition.ValueCount; i++)
            {
                sb.AppendLine($"    {Database.EnumOptions[enumDefinition.FirstValueIndex + i].ToString(Database)},");
            }

            sb.AppendLine("}");

            File.WriteAllText(Path.Combine(path, "Enums", fileName), sb.ToString());
        }
    }

    private void GenerateTypes(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "Types"));
        for (var structIndex = 0; structIndex < Database.StructDefinitions.Length; structIndex++)
        {
            var structDefinition = Database.StructDefinitions[structIndex];
            var fileName = structDefinition.GetName(Database) + ".cs";
            var sb = new StringBuilder();

            sb.AppendLine("using StarBreaker.Common;");
            sb.AppendLine("using StarBreaker.DataCore;");
            sb.AppendLine();
            sb.AppendLine($"namespace {_namespace};");
            sb.AppendLine();

            // Record definition with interface
            if (structDefinition.ParentTypeIndex != -1)
            {
                var parent = Database.StructDefinitions[structDefinition.ParentTypeIndex];
                sb.AppendLine($"public record {structDefinition.GetName(Database)} : {parent.GetName(Database)}, IDataCoreTypedReadable<{structDefinition.GetName(Database)}>");
            }
            else
            {
                sb.AppendLine($"public record {structDefinition.GetName(Database)} : IDataCoreTypedReadable<{structDefinition.GetName(Database)}>");
            }

            sb.AppendLine("{");

            // Properties
            var properties = Database.PropertyDefinitions.AsSpan(structDefinition.FirstAttributeIndex, structDefinition.AttributeCount);
            foreach (var property in properties)
            {
                var propertyType = GetPropertyType(property);
                var name = property.GetName(Database);
                sb.AppendLine($"    public required {propertyType} @{name} {{ get; init; }}");
            }

            sb.AppendLine();

            // Read method
            WriteReadMethod(sb, structDefinition, structIndex);

            sb.AppendLine("}");

            File.WriteAllText(Path.Combine(path, "Types", fileName), sb.ToString());
        }
    }

    private string GetScalarPropertyType(DataCorePropertyDefinition property) => property.DataType switch
    {
        DataType.Boolean => "bool",
        DataType.Byte => "byte",
        DataType.SByte => "sbyte",
        DataType.Int16 => "short",
        DataType.UInt16 => "ushort",
        DataType.Int32 => "int",
        DataType.UInt32 => "uint",
        DataType.Int64 => "long",
        DataType.UInt64 => "ulong",
        DataType.Single => "float",
        DataType.Double => "double",
        DataType.Guid => "CigGuid",
        DataType.Locale => "string",
        DataType.String => "string",

        DataType.EnumChoice => Database.EnumDefinitions[property.StructIndex].GetName(Database),
        DataType.Reference => $"DataCoreRef<{Database.StructDefinitions[property.StructIndex].GetName(Database)}>?",
        DataType.StrongPointer => $"DataCoreRef<{Database.StructDefinitions[property.StructIndex].GetName(Database)}>?",
        DataType.WeakPointer => $"DataCoreRef<{Database.StructDefinitions[property.StructIndex].GetName(Database)}>?",
        DataType.Class => Database.StructDefinitions[property.StructIndex].GetName(Database),

        _ => throw new ArgumentOutOfRangeException()
    };

    private string GetGenericPropertyType(DataCorePropertyDefinition property) => property.DataType switch
    {
        DataType.Boolean => "bool",
        DataType.Byte => "byte",
        DataType.SByte => "sbyte",
        DataType.Int16 => "short",
        DataType.UInt16 => "ushort",
        DataType.Int32 => "int",
        DataType.UInt32 => "uint",
        DataType.Int64 => "long",
        DataType.UInt64 => "ulong",
        DataType.Single => "float",
        DataType.Double => "double",
        DataType.Guid => "CigGuid",
        DataType.Locale => "string",
        DataType.String => "string",

        DataType.EnumChoice => Database.EnumDefinitions[property.StructIndex].GetName(Database),
        DataType.Reference => Database.StructDefinitions[property.StructIndex].GetName(Database),
        DataType.StrongPointer => Database.StructDefinitions[property.StructIndex].GetName(Database),
        DataType.WeakPointer => Database.StructDefinitions[property.StructIndex].GetName(Database),
        DataType.Class => Database.StructDefinitions[property.StructIndex].GetName(Database),

        _ => throw new ArgumentOutOfRangeException()
    };

    private string GetArrayPropertyType(DataCorePropertyDefinition property) => property.DataType switch
    {
        DataType.Boolean => "bool[]",
        DataType.Byte => "byte[]",
        DataType.SByte => "sbyte[]",
        DataType.Int16 => "short[]",
        DataType.UInt16 => "ushort[]",
        DataType.Int32 => "int[]",
        DataType.UInt32 => "uint[]",
        DataType.Int64 => "long[]",
        DataType.UInt64 => "ulong[]",
        DataType.Single => "float[]",
        DataType.Double => "double[]",
        DataType.Guid => "CigGuid[]",
        DataType.Locale => "string[]",
        DataType.String => "string[]",

        DataType.EnumChoice => $"{Database.EnumDefinitions[property.StructIndex].GetName(Database)}[]",
        DataType.Reference => $"DataCoreRef<{Database.StructDefinitions[property.StructIndex].GetName(Database)}>?[]",
        DataType.StrongPointer => $"DataCoreRef<{Database.StructDefinitions[property.StructIndex].GetName(Database)}>?[]",
        DataType.WeakPointer => $"DataCoreRef<{Database.StructDefinitions[property.StructIndex].GetName(Database)}>?[]",
        DataType.Class => $"{Database.StructDefinitions[property.StructIndex].GetName(Database)}[]",

        _ => throw new ArgumentOutOfRangeException()
    };

    private string GetPropertyType(DataCorePropertyDefinition property) => property.ConversionType switch
    {
        ConversionType.Attribute => GetScalarPropertyType(property),
        _ => GetArrayPropertyType(property)
    };

    private void WriteReadMethod(StringBuilder sb, DataCoreStructDefinition structDefinition, int structIndex)
    {
        var typeName = structDefinition.GetName(Database);

        if (structDefinition.ParentTypeIndex != -1)
            sb.AppendLine($"    public new static {typeName} Read(DataCoreTypedReader dataCore, ref SpanReader reader)");
        else
            sb.AppendLine($"    public static {typeName} Read(DataCoreTypedReader dataCore, ref SpanReader reader)");

        sb.AppendLine("    {");

        var allprops = Database.GetProperties(structIndex).AsSpan();

        foreach (var property in allprops)
        {
            if (property.ConversionType == ConversionType.Attribute)
                WriteSingleRead(sb, property);
            else
                WriteArrayRead(sb, property);
        }

        sb.AppendLine();
        sb.AppendLine($"        return new {typeName}");
        sb.AppendLine("        {");

        foreach (var property in allprops)
        {
            var name = property.GetName(Database);
            sb.AppendLine($"            @{name} = _{name},");
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");
    }

    private void WriteSingleRead(StringBuilder sb, DataCorePropertyDefinition property)
    {
        var propertyType = GetGenericPropertyType(property);
        var name = property.GetName(Database);

        switch (property.DataType)
        {
            case DataType.Class:
                sb.AppendLine($"        var _{name} = {propertyType}.Read(dataCore, ref reader);");
                break;
            case DataType.EnumChoice:
                var enumName = Database.EnumDefinitions[property.StructIndex].GetName(Database);
                sb.AppendLine($"        var _{name} = dataCore.EnumParse(reader.Read<DataCoreStringId>(), {enumName}.__Unknown);");
                break;
            case DataType.Reference:
                sb.AppendLine($"        var _{name} = dataCore.CreateRef<{propertyType}>(reader.Read<DataCoreReference>());");
                break;
            case DataType.StrongPointer:
            case DataType.WeakPointer:
                sb.AppendLine($"        var _{name} = dataCore.CreateRef<{propertyType}>(reader.Read<DataCorePointer>());");
                break;
            case DataType.String:
            case DataType.Locale:
                sb.AppendLine($"        var _{name} = reader.Read<DataCoreStringId>().ToString(dataCore.Database);");
                break;
            case DataType.Boolean:
            case DataType.Byte:
            case DataType.SByte:
            case DataType.Int16:
            case DataType.UInt16:
            case DataType.Int32:
            case DataType.UInt32:
            case DataType.Int64:
            case DataType.UInt64:
            case DataType.Single:
            case DataType.Double:
            case DataType.Guid:
                sb.AppendLine($"        var _{name} = reader.Read<{propertyType}>();");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void WriteArrayRead(StringBuilder sb, DataCorePropertyDefinition property)
    {
        var propertyType = GetGenericPropertyType(property);
        var name = property.GetName(Database);

        switch (property.DataType)
        {
            case DataType.Reference:
                sb.AppendLine($"        var _{name} = dataCore.ReadRefArray<{propertyType}>(ref reader);");
                break;
            case DataType.StrongPointer:
                sb.AppendLine($"        var _{name} = dataCore.ReadStrongRefArray<{propertyType}>(ref reader);");
                break;
            case DataType.WeakPointer:
                sb.AppendLine($"        var _{name} = dataCore.ReadWeakRefArray<{propertyType}>(ref reader);");
                break;
            case DataType.Class:
                sb.AppendLine($"        var _{name} = dataCore.ReadClassArray<{propertyType}>(ref reader, {property.StructIndex});");
                break;
            case DataType.Boolean:
                sb.AppendLine($"        var _{name} = dataCore.ReadBoolArray(ref reader);");
                break;
            case DataType.Byte:
                sb.AppendLine($"        var _{name} = dataCore.ReadByteArray(ref reader);");
                break;
            case DataType.SByte:
                sb.AppendLine($"        var _{name} = dataCore.ReadSByteArray(ref reader);");
                break;
            case DataType.Int16:
                sb.AppendLine($"        var _{name} = dataCore.ReadInt16Array(ref reader);");
                break;
            case DataType.UInt16:
                sb.AppendLine($"        var _{name} = dataCore.ReadUInt16Array(ref reader);");
                break;
            case DataType.Int32:
                sb.AppendLine($"        var _{name} = dataCore.ReadInt32Array(ref reader);");
                break;
            case DataType.UInt32:
                sb.AppendLine($"        var _{name} = dataCore.ReadUInt32Array(ref reader);");
                break;
            case DataType.Int64:
                sb.AppendLine($"        var _{name} = dataCore.ReadInt64Array(ref reader);");
                break;
            case DataType.UInt64:
                sb.AppendLine($"        var _{name} = dataCore.ReadUInt64Array(ref reader);");
                break;
            case DataType.Single:
                sb.AppendLine($"        var _{name} = dataCore.ReadSingleArray(ref reader);");
                break;
            case DataType.Double:
                sb.AppendLine($"        var _{name} = dataCore.ReadDoubleArray(ref reader);");
                break;
            case DataType.Guid:
                sb.AppendLine($"        var _{name} = dataCore.ReadGuidArray(ref reader);");
                break;
            case DataType.Locale:
                sb.AppendLine($"        var _{name} = dataCore.ReadLocaleArray(ref reader);");
                break;
            case DataType.String:
                sb.AppendLine($"        var _{name} = dataCore.ReadStringArray(ref reader);");
                break;
            case DataType.EnumChoice:
                sb.AppendLine($"        var _{name} = dataCore.ReadEnumArray<{Database.EnumDefinitions[property.StructIndex].GetName(Database)}>(ref reader);");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}

using StarBreaker.Common;

namespace StarBreaker.DataCore;

/// <summary>
/// A wrapper for a typed DataCore record containing metadata and the strongly-typed data.
/// </summary>
public record DataCoreTypedRecord(string FileName, string Name, CigGuid Id, IDataCoreTypedReadable Data);

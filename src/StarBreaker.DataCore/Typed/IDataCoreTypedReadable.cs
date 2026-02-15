using StarBreaker.Common;

namespace StarBreaker.DataCore;

/// <summary>
/// Marker interface for all generated DataCore types.
/// </summary>
public interface IDataCoreTypedReadable;

/// <summary>
/// Interface for generated DataCore types that can be read from a binary stream.
/// Each generated type implements this with a static Read method.
/// </summary>
/// <typeparam name="TSelf">The implementing type itself</typeparam>
public interface IDataCoreTypedReadable<TSelf> : IDataCoreTypedReadable
    where TSelf : class, IDataCoreTypedReadable<TSelf>
{
    static abstract TSelf Read(DataCoreTypedReader reader, ref SpanReader spanReader);
}

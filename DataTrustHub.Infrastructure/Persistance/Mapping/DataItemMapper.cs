using DataTrustHub.Domain.DataItems;
using DataTrustHub.Infrastructure.Persistance.Model;
using Riok.Mapperly.Abstractions;

namespace DataTrustHub.Infrastructure.Persistance.Mapping;

[Mapper]
internal static partial class DataItemMapper
{
    internal static partial DataItem ToDomain(this DbDataItem source);

    [MapperIgnoreSource(nameof(DataItem.IsClassified))]
    internal static partial DbDataItem ToDb(this DataItem source);

    // Copies onto a tracked row; the key never changes
    [MapperIgnoreSource(nameof(DataItem.IsClassified))]
    [MapperIgnoreSource(nameof(DataItem.Id))]
    [MapperIgnoreTarget(nameof(DbDataItem.Id))]
    internal static partial void CopyTo(this DataItem source, [MappingTarget] DbDataItem target);
}

using Microsoft.Xna.Framework;
using Trinity.Encore.Framework.Game.Partitioning;

namespace Trinity.Encore.Framework.Game.Entities
{
    public interface IEntity
    {
        EntityGuid Guid { get; }
    }
}
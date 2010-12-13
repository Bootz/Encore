using FluentNHibernate.Conventions;
using FluentNHibernate.Conventions.Instances;

namespace Trinity.Encore.Framework.Game.Database.Conventions.Identity
{
    internal sealed class IdConvention : IIdConvention
    {
        public void Apply(IIdentityInstance instance)
        {
            instance.Not.Nullable();
            instance.Unique();
        }
    }
}

﻿using System.Diagnostics.Contracts;
using Trinity.Encore.Game.Achievements;

namespace Trinity.Encore.Game.IO.Formats.Databases.DBC
{
    [ContractVerification(false)]
    public sealed class AchievementRecord : IClientDbRecord
    {
        public int Id { get; set; }

        public AchievementFaction Faction { get; set; }

        public int MapId { get; set; }

        public int ParentId { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public int CategoryId { get; set; }

        public int Points { get; set; }

        public int Order { get; set; }

        public AchievementFlags Flags { get; set; }

        public int Icon { get; set; }

        public string Reward { get; set; }

        public int Count { get; set; }

        public int ReferencedAchievementId { get; set; }
    }
}

using System.Collections.Generic;
using DarkwoodMP.World.Skills;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The game's skill tiers (Resources/SkillTiers, as SkillsMenu loads
    /// it), found once and kept as long as the game (ModHost.Tiers), with
    /// their facts for the rules.
    /// </summary>
    internal sealed class SkillTierCache
    {
        private SkillTiers _tiers;
        private TierFacts[] _facts;

        /// <summary>The tiers, or null before the world is up.</summary>
        public List<SkillTiers.Tier> Tiers
        {
            get
            {
                if (_tiers == null)
                {
                    _facts = null;
                    var menu = Singleton<SkillsMenu>.Instance;
                    if (menu != null)
                    {
                        _tiers = AccessTools.Field(
                            typeof(SkillsMenu),
                            "skillTiers").GetValue(menu) as SkillTiers;
                    }

                    if (_tiers == null)
                    {
                        var go = Resources.Load("SkillTiers") as GameObject;
                        if (go != null)
                        {
                            _tiers = go.GetComponent<SkillTiers>();
                        }
                    }
                }

                if (_tiers != null)
                {
                    return _tiers.tiers;
                }

                return null;
            }
        }

        /// <summary>
        /// The tiers' facts (SkillTree.FactsOf), made once the tiers are
        /// found; null before.
        /// </summary>
        public TierFacts[] Facts
        {
            get
            {
                var tiers = Tiers;
                if (tiers == null)
                {
                    return null;
                }

                if (_facts == null)
                {
                    _facts = SkillTree.FactsOf(tiers);
                }

                return _facts;
            }
        }
    }
}

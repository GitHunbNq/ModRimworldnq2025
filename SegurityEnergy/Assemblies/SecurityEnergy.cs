using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using System.Collections.Generic;
using System.Linq;

namespace SegurityEnergy
{
    // --- CLASES ORIGINALES DEL MOD ---
    public class CompProperties_Stunnable : CompProperties
    {
        public List<DamageDef> affectedDamageDefs;
        public bool useLargeEMPEffecter;
        public float empChancePerTick;
        public CompProperties_Stunnable()
        {
            this.compClass = typeof(CompStunnable);
        }
    }
    public class CompStunnable : ThingComp
    {
        private CompProperties_Stunnable Props => (CompProperties_Stunnable)this.props;
        private int empTicks;
        public override void CompTick()
        {
            base.CompTick();
            if (this.parent.Spawned && this.empTicks > 0)
            {
                this.empTicks--;
                if (this.empTicks == 0)
                {
                    if (this.Props.useLargeEMPEffecter)
                    {
                        GenExplosion.DoExplosion(this.parent.Position, this.parent.Map, 5f, DamageDefOf.EMP, null, -1, -1f, null, null, null, null, null, 0f, 1, false, null, 0f, 1);
                    }
                    else
                    {
                        FleckMaker.Static(this.parent.Position, this.parent.Map, FleckDefOf.PsycastAreaEffect, 0.5f);
                    }
                }
            }
        }
        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            if (this.Props.affectedDamageDefs.Contains(dinfo.Def))
            {
                this.empTicks += (int)(dinfo.Amount * 10f);
                absorbed = true;
                if (this.empTicks > 600)
                {
                    this.empTicks = 600;
                }
            }
        }
    }
    public class DamageWorker_Stun : DamageWorker
    {
        private const float WetGroundRadius = 3f;
        public override DamageResult Apply(DamageInfo dinfo, Thing victim)
        {
            DamageResult result = new DamageResult();
            if (victim is Pawn pawn && !pawn.Dead && !pawn.Downed)
            {
                Map map = pawn.Map;
                bool isWetGround = IsGroundWet(pawn.Position, map);
                bool isTrap = dinfo.Instigator is Building_FloorStunTrap;
                if (isWetGround && !isTrap) 
                {
                    FleckMaker.Static(pawn.Position, map, FleckDefOf.ShotFlash, 10f);
                    foreach (Thing thing in GenRadial.RadialDistinctThingsAround(pawn.Position, map, WetGroundRadius, true))
                    {
                        if (thing is Pawn nearbyPawn && !nearbyPawn.Dead && !nearbyPawn.Downed)
                        {
                            ApplyStunEffects(nearbyPawn, dinfo);
                            result.hitThing = nearbyPawn;
                        }
                    }
                }
                else
                {
                    ApplyStunEffects(pawn, dinfo);
                    result.hitThing = pawn;
                }
            }
            return result;
        }
        private void ApplyStunEffects(Pawn pawn, DamageInfo dinfo)
        {
            bool isTrap = dinfo.Instigator is Building_FloorStunTrap;
            Log.Message($"Applying StunCustom to {pawn.Name} via {(isTrap ? "FloorStunTrap" : "Turret/Panel")}");
            Hediff stunHediff = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("StunCustom"), pawn);
            stunHediff.Severity = 1f;
            pawn.health.AddHediff(stunHediff, null, dinfo);
            if (isTrap)
            {
                if (pawn.Map != null)
                {
                    MoteMaker.MakeStaticMote(pawn.Position, pawn.Map, ThingDef.Named("Mote_BeamRepeaterLaser"), 1f);
                }
            }
            else
            {
                float maxHealth = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness) * 100f;
                float damageAmount = maxHealth * 0.2f; 
                DamageInfo stunDinfo = new DamageInfo(DamageDefOf.Blunt, damageAmount, 0f, -1f, dinfo.Instigator);
                pawn.TakeDamage(stunDinfo);
                DamageInfo cutDinfo = new DamageInfo(DamageDefOf.Cut, 2f, 0f, -1f, dinfo.Instigator, pawn.RaceProps.body.corePart);
                pawn.TakeDamage(cutDinfo);
                Hediff burnHediff = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Burn"), pawn, pawn.RaceProps.body.corePart);
                burnHediff.Severity = 0.1f;
                pawn.health.AddHediff(burnHediff);
            }
            pawn.health.capacities.Notify_CapacityLevelsDirty();
        }
        private bool IsGroundWet(IntVec3 position, Map map)
        {
            if (map == null) return false;
            if (map.weatherManager.RainRate > 0) return true;
            TerrainDef terrain = map.terrainGrid.TerrainAt(position);
            return terrain != null && (terrain == TerrainDefOf.WaterShallow || terrain == TerrainDefOf.WaterDeep || terrain.defName.Contains("Water"));
        }
    }

    // --- NUEVO COMPONENTE DE CARGA ---
    public class CompProperties_Rechargeable : CompProperties
    {
        public int maxCharge = 5;
        public int ticksToRechargeOneCharge = 180;
        public float energyConsumptionWhenCharging = 40f;
        public float energyConsumptionWhenFull = 25f;

        public CompProperties_Rechargeable()
        {
            this.compClass = typeof(CompRechargeable);
        }
    }

    public class CompRechargeable : ThingComp
    {
        private CompProperties_Rechargeable Props => (CompProperties_Rechargeable)this.props;
        private CompPowerTrader powerComp;
        private int currentCharge = 0;
        private int ticksSinceLastUse = 0;

        public int CurrentCharge => currentCharge;
        public int MaxCharge => Props.maxCharge;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            this.powerComp = this.parent.GetComp<CompPowerTrader>();
        }

        public override void CompTick()
        {
            base.CompTick();
            
            if (this.powerComp != null)
            {
                if (currentCharge < MaxCharge)
                {
                    this.powerComp.powerOutput = -Props.energyConsumptionWhenCharging;
                }
                else
                {
                    this.powerComp.powerOutput = -Props.energyConsumptionWhenFull;
                }
            }

            if (currentCharge < MaxCharge && this.powerComp != null && this.powerComp.PowerOn)
            {
                ticksSinceLastUse++;
                if (ticksSinceLastUse >= Props.ticksToRechargeOneCharge)
                {
                    currentCharge++;
                    ticksSinceLastUse = 0;
                }
            }
        }

        public bool TryUseCharge()
        {
            if (currentCharge > 0)
            {
                currentCharge--;
                ticksSinceLastUse = 0;
                return true;
            }
            return false;
        }

        public override string CompInspectStringExtra()
        {
            return "Carga: " + (currentCharge * 100 / MaxCharge) + "% (" + currentCharge + "/" + MaxCharge + ")";
        }
    }

    // --- CLASE DE TRAMPA DE PISO REFACTORIZADA PARA USAR EL COMPONENTE ---
    public class Building_FloorStunTrap : Building_Trap
    {
        private CompRechargeable rechargeableComp;
        private const float AoERadius = 2f;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            this.rechargeableComp = this.GetComp<CompRechargeable>();
        }

        public override bool CheckSpring(Pawn p)
        {
            bool shouldActivate = false;
            if (p != null)
            {
                if (p.IsPrisonerOfColony || (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)) || (p.RaceProps.Animal && p.HostileTo(Faction.OfPlayer)))
                {
                    shouldActivate = true;
                }
            }

            if (shouldActivate)
            {
                // Ahora usamos el componente para verificar y usar la carga.
                if (rechargeableComp != null && rechargeableComp.TryUseCharge())
                {
                    ApplyAreaEffect();
                    SoundDefOf.TrapSpike_Activate.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
                    return true;
                }
            }
            return false;
        }

        private void ApplyAreaEffect()
        {
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(base.Position, base.Map, AoERadius, true))
            {
                Pawn p = thing as Pawn;
                if (p != null)
                {
                    bool isTarget = p.IsPrisonerOfColony || (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)) || (p.RaceProps.Animal && p.HostileTo(Faction.OfPlayer));
                    if (isTarget)
                    {
                        DamageDef myStunDamageDef = DefDatabase<DamageDef>.GetNamed("StunCustomDamage", true);
                        DamageInfo dinfo = new DamageInfo(myStunDamageDef, 0f, 0f, -1f, this);
                        p.TakeDamage(dinfo);
                    }
                }
            }
        }
    }

    // --- EJEMPLO DE CÓMO LA TORRETA USARÍA EL COMPONENTE ---
    public class Building_RayTurret : Building_TurretGun
    {
        private CompRechargeable rechargeableComp;
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            this.rechargeableComp = this.GetComp<CompRechargeable>();
        }

        public override bool CanSetFor
        {
            get
            {
                // La torreta solo puede disparar si tiene carga.
                return rechargeableComp != null && rechargeableComp.CurrentCharge > 0;
            }
        }

        public override void BurstStart()
        {
            // Usa una carga al empezar el disparo.
            if (rechargeableComp != null)
            {
                rechargeableComp.TryUseCharge();
            }
            base.BurstStart();
        }
    }
}
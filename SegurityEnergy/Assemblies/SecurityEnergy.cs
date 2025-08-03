using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using System.Collections.Generic;

namespace SegurityEnergy
{
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
                        GenExplosion.DoExplosion(
                            center: this.parent.Position,
                            map: this.parent.Map,
                            radius: 5f,
                            damType: DamageDefOf.EMP,
                            instigator: null,
                            damAmount: -1,
                            armorPenetration: -1f,
                            explosionSound: null,
                            weapon: null,
                            projectile: null,
                            intendedTarget: null,
                            doVisualEffects: true,
                            propagationSpeed: 0.6f,
                            excludeRadius: 0f,
                            affectedAngle: null,
                            doSound: true
                        );
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
            if (this.Props.affectedDamageDefs != null && this.Props.affectedDamageDefs.Contains(dinfo.Def))
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
            Pawn pawn = victim as Pawn;
            if (pawn != null && !pawn.Dead && !pawn.Downed)
            {
                Map map = pawn.Map;
                bool isWetGround = IsGroundWet(pawn.Position, map);
                bool isTrap = dinfo.Instigator is Building || dinfo.Instigator?.def.defName.Contains("Trap") == true;
                bool isTurret = dinfo.Instigator is Building_Turret || dinfo.Instigator?.def.defName.Contains("Turret") == true;
                if (isWetGround && !isTrap && !isTurret)
                {
                    FleckMaker.Static(pawn.Position, map, FleckDefOf.ShotFlash, 10f);
                    foreach (Thing thing in GenRadial.RadialDistinctThingsAround(pawn.Position, map, WetGroundRadius, true))
                    {
                        Pawn nearbyPawn = thing as Pawn;
                        if (nearbyPawn != null && !nearbyPawn.Dead && !nearbyPawn.Downed)
                        {
                            ApplyStunEffects(nearbyPawn, dinfo.Instigator);
                            result.hitThing = nearbyPawn;
                        }
                    }
                }
                else
                {
                    ApplyStunEffects(pawn, dinfo.Instigator);
                    result.hitThing = pawn;
                }
            }
            return result;
        }

        public static void ApplyStunEffects(Pawn pawn, Thing instigator)
        {
            bool isTrap = instigator is Building || instigator?.def.defName.Contains("Trap") == true;
            bool isTurret = instigator is Building_Turret || instigator?.def.defName.Contains("Turret") == true;
            Log.Message($"[SegurityEnergy] Applying StunCustom to {pawn.Name} via {(isTrap ? "Trap" : isTurret ? "Turret" : "Unknown")}");
            Hediff stunHediff = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("StunCustom", true), pawn);
            stunHediff.Severity = 1f;
            pawn.health.AddHediff(stunHediff);
            if (pawn.Map != null)
            {
                ThingDef moteDef = DefDatabase<ThingDef>.GetNamed("Mote_BeamRepeaterLaser", false) ?? ThingDefOf.Mote_DustPuff;
                MoteMaker.MakeStaticMote(pawn.Position, pawn.Map, moteDef, 1f);
            }
            pawn.health.capacities.Notify_CapacityLevelsDirty();
        }

        private bool IsGroundWet(IntVec3 position, Map map)
        {
            if (map == null) return false;
            if (map.weatherManager.RainRate > 0f) return true;
            TerrainDef terrain = map.terrainGrid.TerrainAt(position);
            return terrain != null && (terrain == TerrainDefOf.WaterShallow || terrain == TerrainDefOf.WaterDeep || terrain.defName.Contains("Water"));
        }
    }

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
            if (this.powerComp == null)
            {
                Log.Error($"[SegurityEnergy] CompRechargeable at {this.parent.Position} failed to find CompPowerTrader.");
            }
            if (!respawningAfterLoad)
            {
                currentCharge = 0;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            if (this.powerComp == null)
            {
                Log.Warning($"[SegurityEnergy] CompRechargeable at {this.parent.Position} has null powerComp.");
                return;
            }
            if (currentCharge < MaxCharge)
            {
                this.powerComp.PowerOutput = -Props.energyConsumptionWhenCharging;
            }
            else
            {
                this.powerComp.PowerOutput = -Props.energyConsumptionWhenFull;
            }

            if (currentCharge < MaxCharge && this.powerComp.PowerOn)
            {
                ticksSinceLastUse++;
                if (ticksSinceLastUse >= Props.ticksToRechargeOneCharge)
                {
                    currentCharge++;
                    ticksSinceLastUse = 0;
                    Log.Message($"[SegurityEnergy] CompRechargeable at {this.parent.Position} recharged to {currentCharge}/{MaxCharge}");
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentCharge, "currentCharge", 0);
            Scribe_Values.Look(ref ticksSinceLastUse, "ticksSinceLastUse", 0);
        }

        public bool TryUseCharge()
        {
            if (currentCharge > 0)
            {
                currentCharge--;
                ticksSinceLastUse = 0;
                Log.Message($"[SegurityEnergy] CompRechargeable at {this.parent.Position} used charge. Remaining: {currentCharge}/{MaxCharge}");
                return true;
            }
            Log.Message($"[SegurityEnergy] CompRechargeable at {this.parent.Position} failed to use charge: {currentCharge}/{MaxCharge}");
            return false;
        }

        public override string CompInspectStringExtra()
        {
            if (powerComp == null || !powerComp.PowerOn)
            {
                return $"Carga: {currentCharge}/{MaxCharge}\nNo Power";
            }
            if (currentCharge < MaxCharge && ticksSinceLastUse > 0)
            {
                float rechargePercent = (ticksSinceLastUse / (float)Props.ticksToRechargeOneCharge) * 100f;
                return $"Carga: {currentCharge}/{MaxCharge}\nCharging: {rechargePercent:F0}%";
            }
            return $"Carga: {currentCharge}/{MaxCharge}\n{(currentCharge == MaxCharge ? "Fully Charged" : "Ready to Recharge")}";
        }
    }

    public class Building_FloorStunTrap : Building_Trap
    {
        private CompRechargeable rechargeableComp;
        private const float AoERadius = 2f;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            this.rechargeableComp = this.GetComp<CompRechargeable>();
            if (this.rechargeableComp == null)
            {
                Log.Error($"[SegurityEnergy] Building_FloorStunTrap at {this.Position} failed to initialize CompRechargeable.");
            }
        }

        protected override void SpringSub(Pawn p)
        {
            if (p == null || p.Dead || !p.HostileTo(this.Faction) || rechargeableComp == null || !rechargeableComp.TryUseCharge())
            {
                Log.Message($"[SegurityEnergy] Building_FloorStunTrap at {this.Position} failed to activate: " +
                            $"Pawn: {p?.Name?.ToString() ?? "null"}, Dead: {p?.Dead ?? false}, Hostile: {p?.HostileTo(this.Faction) ?? false}, " +
                            $"Charge: {rechargeableComp?.CurrentCharge ?? 0}");
                return;
            }

            ApplyAreaEffect();
            SoundDefOf.TrapSpring.PlayOneShot(new TargetInfo(this.Position, this.Map));
        }

        private void ApplyAreaEffect()
        {
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(this.Position, this.Map, AoERadius, true))
            {
                if (thing is Pawn p && !p.Dead && !p.Downed)
                {
                    bool isTarget = p.IsPrisonerOfColony || (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)) || (p.RaceProps.Animal && p.HostileTo(Faction.OfPlayer));
                    if (isTarget)
                    {
                        DamageWorker_Stun.ApplyStunEffects(p, this);
                        Log.Message($"[SegurityEnergy] Building_FloorStunTrap at {this.Position} stunned {p.Name}");
                    }
                }
            }
        }

        public override string GetInspectString()
        {
            return rechargeableComp?.CompInspectStringExtra() ?? "Carga: 0/5\nNo Power";
        }
    }

    public class Building_FloorRayTrap : Building_Trap
    {
        private CompRechargeable rechargeableComp;
        private const float AoERadius = 2f;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            this.rechargeableComp = this.GetComp<CompRechargeable>();
            if (this.rechargeableComp == null)
            {
                Log.Error($"[SegurityEnergy] Building_FloorRayTrap at {this.Position} failed to initialize CompRechargeable.");
            }
        }

        protected override void SpringSub(Pawn p)
        {
            if (p == null || p.Dead || !p.HostileTo(this.Faction) || rechargeableComp == null || !rechargeableComp.TryUseCharge())
            {
                Log.Message($"[SegurityEnergy] Building_FloorRayTrap at {this.Position} failed to activate: " +
                            $"Pawn: {p?.Name?.ToString() ?? "null"}, Dead: {p?.Dead ?? false}, Hostile: {p?.HostileTo(this.Faction) ?? false}, " +
                            $"Charge: {rechargeableComp?.CurrentCharge ?? 0}");
                return;
            }

            ApplyAreaEffect();
            SoundDefOf.TrapSpring.PlayOneShot(new TargetInfo(this.Position, this.Map));
        }

        private void ApplyAreaEffect()
        {
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(this.Position, this.Map, AoERadius, true))
            {
                if (thing is Pawn p && !p.Dead && !p.Downed)
                {
                    bool isTarget = p.IsPrisonerOfColony || (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)) || (p.RaceProps.Animal && p.HostileTo(Faction.OfPlayer));
                    if (isTarget)
                    {
                        DamageWorker_Stun.ApplyStunEffects(p, this);
                        Log.Message($"[SegurityEnergy] Building_FloorRayTrap at {this.Position} stunned {p.Name}");
                    }
                }
            }
        }

        public override string GetInspectString()
        {
            return rechargeableComp?.CompInspectStringExtra() ?? "Carga: 0/5\nNo Power";
        }
    }

    public class Building_RayTurret : Building_TurretGun
    {
        private CompRechargeable rechargeableComp;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            this.rechargeableComp = this.GetComp<CompRechargeable>();
            if (this.rechargeableComp == null)
            {
                Log.Error($"[SegurityEnergy] Building_RayTurret at {this.Position} failed to initialize CompRechargeable.");
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (this.rechargeableComp != null && this.rechargeableComp.CurrentCharge > 0 && this.AttackVerb != null &&
                this.CurrentTarget != null && this.CurrentTarget.IsValid)
            {
                Verb attackVerb = this.AttackVerb;
                if (attackVerb != null && attackVerb.verbProps != null)
                {
                    LocalTargetInfo target = this.CurrentTarget;
                    if (target.Thing is Pawn p && !p.Dead && !p.Downed)
                    {
                        bool isTarget = p.IsPrisonerOfColony || (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)) || (p.RaceProps.Animal && p.HostileTo(Faction.OfPlayer));
                        if (isTarget && attackVerb.TryStartCastOn(target, false, true, false))
                        {
                            this.rechargeableComp.TryUseCharge();
                            DamageWorker_Stun.ApplyStunEffects(p, this);
                            Log.Message($"[SegurityEnergy] Building_RayTurret at {this.Position} stunned {p.Name}");
                        }
                        else
                        {
                            Log.Message($"[SegurityEnergy] Building_RayTurret at {this.Position} failed to fire: " +
                                        $"Verb: {attackVerb != null}, Target: {target.Thing?.Label ?? "null"}");
                        }
                    }
                }
                else
                {
                    Log.Error($"[SegurityEnergy] Building_RayTurret at {this.Position} has null AttackVerb or verbProps.");
                }
            }
        }

        public override string GetInspectString()
        {
            return rechargeableComp?.CompInspectStringExtra() ?? "Carga: 0/5\nNo Power";
        }
    }
}
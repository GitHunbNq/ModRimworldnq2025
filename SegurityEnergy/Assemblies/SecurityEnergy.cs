using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI; // Required for Verb and TryStartCastOn
using Verse.Sound;
using System.Collections.Generic;
using System.Linq;

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
            if (victim is Pawn pawn && !pawn.Dead && !pawn.Downed)
            {
                Map map = pawn.Map;
                bool isWetGround = IsGroundWet(pawn.Position, map);
                bool isTrap = dinfo.Instigator is Building_FloorStunTrap || dinfo.Instigator is Building_FloorRayTrap;
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
            bool isTrap = dinfo.Instigator is Building_FloorStunTrap || dinfo.Instigator is Building_FloorRayTrap;
            Log.Message($"[SegurityEnergy] Applying StunCustom to {pawn.Name} via {(isTrap ? "Trap" : "Turret/Panel")}");
            Hediff stunHediff = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("StunCustom", false), pawn);
            if (stunHediff != null)
            {
                stunHediff.Severity = 1f;
                pawn.health.AddHediff(stunHediff, null, dinfo);
            }
            else
            {
                Log.Error($"[SegurityEnergy] StunCustom hediff not found in DefDatabase.");
            }
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
                Hediff burnHediff = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Burn", false), pawn, pawn.RaceProps.body.corePart);
                if (burnHediff != null)
                {
                    burnHediff.Severity = 0.1f;
                    pawn.health.AddHediff(burnHediff);
                }
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
                            $"Pawn: {p?.Name.ToString() ?? "null"}, Dead: {p?.Dead ?? false}, Hostile: {p?.HostileTo(this.Faction) ?? false}, " +
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
                        DamageDef myStunDamageDef = DefDatabase<DamageDef>.GetNamed("StunCustomDamage", false);
                        if (myStunDamageDef != null)
                        {
                            DamageInfo dinfo = new DamageInfo(myStunDamageDef, 1f, 0f, -1f, this);
                            p.TakeDamage(dinfo);
                            Log.Message($"[SegurityEnergy] Building_FloorStunTrap at {this.Position} stunned {p.Name}");
                        }
                        else
                        {
                            Log.Error($"[SegurityEnergy] StunCustomDamage not found in DefDatabase.");
                        }
                    }
                }
            }
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
                            $"Pawn: {p?.Name.ToString() ?? "null"}, Dead: {p?.Dead ?? false}, Hostile: {p?.HostileTo(this.Faction) ?? false}, " +
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
                        DamageDef myStunDamageDef = DefDatabase<DamageDef>.GetNamed("StunCustomDamage", false);
                        if (myStunDamageDef != null)
                        {
                            DamageInfo dinfo = new DamageInfo(myStunDamageDef, 1f, 0f, -1f, this);
                            p.TakeDamage(dinfo);
                            if (this.Map != null)
                            {
                                MoteMaker.MakeStaticMote(p.Position, this.Map, ThingDef.Named("Mote_BeamRepeaterLaser"), 1f);
                            }
                            Log.Message($"[SegurityEnergy] Building_FloorRayTrap at {this.Position} stunned {p.Name}");
                        }
                        else
                        {
                            Log.Error($"[SegurityEnergy] StunCustomDamage not found in DefDatabase.");
                        }
                    }
                }
            }
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            string chargeStatus = $"Ray Ammo: {rechargeableComp?.CurrentCharge ?? 0}/{rechargeableComp?.MaxCharge ?? 5}";
            string rechargeStatus = rechargeableComp?.CompInspectStringExtra() ?? "No Power";
            return $"{baseString}\n{chargeStatus}\n{rechargeStatus}";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // No Scribe_References for CompRechargeable, as its fields are saved in CompRechargeable.PostExposeData
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
                if (this.AttackVerb.TryStartCastOn(this.CurrentTarget, surpriseAttack: false, canHitNonTargetPawns: true, preventFriendlyFire: false))
                {
                    this.rechargeableComp.TryUseCharge();
                    Log.Message($"[SegurityEnergy] Building_RayTurret at {this.Position} fired at {this.CurrentTarget.Thing?.Label ?? "null"}");
                }
                else
                {
                    Log.Message($"[SegurityEnergy] Building_RayTurret at {this.Position} failed to fire: " +
                                $"Verb: {this.AttackVerb != null}, Target: {this.CurrentTarget.Thing?.Label ?? "null"}");
                }
            }
        }
    }
}
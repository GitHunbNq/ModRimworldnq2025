using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;
using HarmonyLib;
using UnityEngine;
using System.Linq;
using System.Numerics;
using Vector3 = System.Numerics.Vector3;

namespace SecurityEnergy
{
    [StaticConstructorOnStartup]
    public static class StunPatch
    {
        public static bool IsCombatExtendedActive = false;

        static StunPatch()
        {
            var harmony = new Harmony("Nelfox.SecurityEnergy");
            harmony.PatchAll();
            if (ModLister.HasActiveModWithName("Combat Extended"))
            {
                IsCombatExtendedActive = true;
                Log.Message("SecurityEnergy: Combat Extended detected, applying CE compatibility.");
            }
            else
            {
                Log.Message("SecurityEnergy: Running in vanilla mode.");
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

                if (isWetGround && !isTrap) // Efecto de área solo para torretas/paneles
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
                // Efecto visual para la trampa
                if (pawn.Map != null)
                {
                    MoteMaker.MakeStaticMote(pawn.Position, pawn.Map, ThingDef.Named("Mote_BeamRepeaterLaser"), 1f);
                }
            }
            else
            {
                // Daño físico para torretas/paneles
                float maxHealth = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness) * 100f;
                float damageAmount = maxHealth * 0.2f; // 20% de conciencia como daño
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

    [HarmonyPatch(typeof(Building_TurretGun), "TryFindNewTarget")]
    public static class TurretTargetPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Building_TurretGun __instance, ref LocalTargetInfo __result)
        {
            if (__instance.def.defName != "SecurityEnergy" && __instance.def.defName != "Turret_WallStunPanel")
                return;

            if (__result.Thing is Pawn pawn && pawn.Downed)
            {
                __result = LocalTargetInfo.Invalid;
                Log.Message($"Turret at {__instance.Position} ignored downed pawn: {pawn.Name}");
            }
        }
    }

    public class Building_WallStunPanel : Building_TurretGun
    {
        private const float FiringArc = 180f;
        private const float MaxRangeWet = 3f;

        public void Tick()
        {
            base.Tick();
            if (this.Spawned && this.powerComp.PowerOn)
            {
                IntVec3 position = this.Position;
                Map map = this.Map;
                if (map != null && map.weatherManager.RainRate > 0.1f && map.terrainGrid.TerrainAt(position).IsWater)
                {
                    Verb verb = this.AttackVerb;
                    if (verb != null && verb.Available())
                    {
                        verb.verbProps.range = MaxRangeWet;
                    }
                }

                if (this.gun != null && this.CanSetForcedTarget)
                {
                    UpdateTargetIfOutOfArc();
                }
            }
        }

        private void UpdateTargetIfOutOfArc()
        {
            if (this.CurrentTarget != null && (!IsTargetInFiringArc(this.CurrentTarget) || !CanTarget(this.CurrentTarget)))
            {
                Log.Message($"[SecurityEnergy] Descartando objetivo {this.CurrentTarget.Thing?.Label ?? "nulo"} - Fuera de arco o inválido");
                this.currentTargetInt = LocalTargetInfo.Invalid;
            }
        }

        private bool IsTargetInFiringArc(LocalTargetInfo target)
        {
            if (!target.IsValid || !target.HasThing)
            {
                Log.Message($"[SecurityEnergy] Objetivo {target.Thing?.Label ?? "nulo"} no válido o sin cosa");
                return false;
            }

            UnityEngine.Vector3 turretPos = this.Position.ToVector3Shifted();
            UnityEngine.Vector3 targetPos = target.Thing.Position.ToVector3Shifted();
            UnityEngine.Vector3 directionToTarget = (targetPos - turretPos).normalized;
            UnityEngine.Vector3 forwardDirection = this.Rotation.AsVector2.normalized;
            float angle = UnityEngine.Vector3.Angle(forwardDirection,
                                        directionToTarget);

            bool inArc = angle <= (FiringArc / 2f);
            Log.Message($"[SecurityEnergy] Verificando arco para {target.Thing.Label}: Ángulo = {angle}, En arco = {inArc}");
            return inArc;
        }

        public override LocalTargetInfo TryFindNewTarget()
        {
            ThingRequest thingRequest = ThingRequest.ForGroup(ThingRequestGroup.Pawn);
            float range = this.AttackVerb.verbProps.range;

            foreach (Thing potentialTarget in GenRadial.RadialDistinctThingsAround(this.Position, this.Map, range, true))
            {
                if (!thingRequest.Accepts(potentialTarget))
                    continue;

                LocalTargetInfo targetInfo = new LocalTargetInfo(potentialTarget);
                if (CanTarget(targetInfo) && IsTargetInFiringArc(targetInfo))
                {
                    Log.Message($"[SecurityEnergy] Objetivo seleccionado: {targetInfo.Thing.Label}");
                    return targetInfo;
                }
            }

            Log.Message("[SecurityEnergy] No se encontró ningún objetivo válido");
            return LocalTargetInfo.Invalid;
        }

        private bool CanTarget(LocalTargetInfo target)
        {
            if (!target.IsValid || !target.HasThing || !this.AttackVerb.CanHitTarget(target))
            {
                Log.Message($"[SecurityEnergy] Objetivo {target.Thing?.Label ?? "nulo"} no válido, sin cosa o fuera de alcance");
                return false;
            }

            Pawn pawn = target.Thing as Pawn;
            if (pawn == null || !pawn.HostileTo(this.Faction) || pawn.Downed)
            {
                if (pawn != null)
                {
                    if (pawn.Downed)
                        Log.Message($"[SecurityEnergy] Objetivo {pawn.Label} descartado: está desmayado");
                    else if (!pawn.HostileTo(this.Faction))
                        Log.Message($"[SecurityEnergy] Objetivo {pawn.Label} descartado: no es hostil");
                }
                return false;
            }

            Log.Message($"[SecurityEnergy] Objetivo {pawn.Label} válido para disparar");
            return true;
        }
    }

    [HarmonyPatch(typeof(Building_TurretGun), "TryStartShootSomething")]
    public static class Building_TurretGun_TryStartShootSomething_Patch
    {
        public static void Prefix(Building_TurretGun __instance, bool canBeginBurstImmediately)
        {
            if (__instance is Building_WallStunPanel panel)
            {
                Log.Message($"[SecurityEnergy] Intentando disparar a {panel.CurrentTarget.Thing?.Label ?? "nulo"} desde {panel.Label} en {panel.Position}");
            }
        }

        public static void Postfix(Building_TurretGun __instance, bool canBeginBurstImmediately)
        {
            if (__instance is Building_WallStunPanel panel)
            {
                Log.Message($"[SecurityEnergy] Disparo procesado para {panel.CurrentTarget.Thing?.Label ?? "nulo"} - Éxito: {panel.AttackVerb.state == VerbState.Idle}");
            }
        }
    }

    public class PlaceWorker_NextToWall : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            Thing thingAtLoc = map.thingGrid.ThingAt(loc, ThingCategory.Building);
            if (thingAtLoc != null && thingAtLoc.def != null &&
                (thingAtLoc.def.fillPercent >= 1.0f || thingAtLoc.def.building?.isNaturalRock == true))
            {
                return new AcceptanceReport("Cannot place on top of another wall, rock, or fixed object.");
            }

            IntVec3 wallPosition = IntVec3.Invalid;
            int wallCount = 0;

            foreach (IntVec3 cell in GenAdj.CellsAdjacentCardinal(loc, rot, new IntVec2(1, 1)))
            {
                Thing building = map.thingGrid.ThingAt(cell, ThingCategory.Building);
                if (building != null && building.def != null &&
                    building.def.building != null &&
                    building.def.fillPercent >= 1.0f &&
                    !building.def.building.isNaturalRock &&
                    (building.def.defName.Contains("Wall") || building.def.building.isEdifice))
                {
                    wallCount++;
                    wallPosition = cell;
                }
            }

            if (wallCount == 0)
            {
                return new AcceptanceReport("Must be placed next to a constructed wall.");
            }
            if (wallCount > 1)
            {
                return new AcceptanceReport("Cannot place between two walls.");
            }

            IntVec3 oppositeCell = loc + (loc - wallPosition);
            if (oppositeCell.InBounds(map))
            {
                Thing oppositeThing = map.thingGrid.ThingAt(oppositeCell, ThingCategory.Building);
                if (oppositeThing != null && oppositeThing.def != null &&
                    (oppositeThing.def.fillPercent >= 1.0f || thingAtLoc.def.building?.isNaturalRock == true) &&
                    oppositeThing.def.defName != "PowerConduit")
                {
                    return new AcceptanceReport("Cannot place on a wall side with an adjacent object.");
                }
            }

            return true;
        }
    }
//				GEMINIS

    public class BuildingTrapSpike : Building_Trap
    {
        private CompPowerTrader powerComp;
        private int currentCharge = 5;
        private int ticksSinceLastActivation = 0;
        private const int MaxCharge = 5;
        private const float EnergyChargeConsumption = 15f;
        private const float EnergyStandbyConsumption = 25f;
        private const int RecargaDelayTicks = 180; // 3 segundos * 60 ticks/segundo

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            this.powerComp = base.GetComp<CompPowerTrader>();
        }

        public void Tick()
        {
            base.Tick();
            
            // Consumo de energía
            if (this.powerComp != null)
            {
                if (currentCharge < MaxCharge)
                {
                    this.powerComp.powerOutputInt = -(EnergyStandbyConsumption + EnergyChargeConsumption);
                }
                else
                {
                    this.powerComp.powerOutputInt = -EnergyStandbyConsumption;
                }
            }
            
            // Lógica de recarga
            if (currentCharge < MaxCharge)
            {
                ticksSinceLastActivation++;
                if (ticksSinceLastActivation >= RecargaDelayTicks)
                {
                    if (this.powerComp != null && this.powerComp.PowerOn)
                    {
                        currentCharge++;
                        ticksSinceLastActivation = 0; // Resetear el temporizador para la siguiente carga.
                    }
                }
            }
        }

        public bool CheckSpring(Pawn p)
        {
            bool shouldActivate = false;
            if (p != null)
            {
                // La trampa se activa con enemigos, animales agresivos y prisioneros.
                if (p.IsPrisonerOfColony || (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)) || (p.RaceProps.Animal && p.HostileTo(Faction.OfPlayer)))
                {
                    shouldActivate = true;
                }
            }

            if (shouldActivate)
            {
                // Se activa si tiene energía y al menos una carga.
                if (this.powerComp != null && this.powerComp.PowerOn && currentCharge > 0)
                {
                    // Resta una carga y reinicia el temporizador de recarga.
                    currentCharge--;
                    ticksSinceLastActivation = 0;

                    // --- CAMBIO CLAVE AQUÍ ---
                    // Ahora utilizamos el nombre correcto de tu HediffDef.
                    HediffDef hediffDef = DefDatabase<HediffDef>.GetNamed("StunCustom", true);
                    if (hediffDef != null)
                    {
                        Hediff hediff = HediffMaker.MakeHediff(hediffDef, p);
                        p.health.AddHediff(hediff);
                    }

                    // Lógica para el daño a Mecanoides
                    if (p.RaceProps.IsMechanoid)
                    {
                        // 50% del daño total de vida.
                        float damageAmount = p.MaxHealth * 0.50f;
                        DamageInfo dinfo = new DamageInfo(DamageDefOf.Crush, damageAmount, 0f, -1f, this);
                        p.TakeDamage(dinfo);
                    }
                    
                    // Emite el sonido de la trampa activándose
                    SoundDefOf.TrapSpike_Activate.PlayOneShot(new TargetInfo(base.Position, base.Map, false));

                    // Muestra la animación de la trampa
                    MoteMaker.MakeStaticMote(base.Position, base.Map, ThingDefOf.Mote_SpikeTrap);
                    
                    return true;
                }
            }
            // La trampa no se activa
            return false;
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            string chargeString = "Carga: " + (currentCharge * 100 / MaxCharge) + "% (" + currentCharge + "/" + MaxCharge + ")";
            return baseString + "\n" + chargeString;
        }
    }


//
    public class Building_FloorStunTrap : Building_Trap
    {
        private const int CooldownTicks = 90; // 1.5 segundos
        private int ticksUntilReady = 0;
        private CompPowerTrader powerCompTrader;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            this.powerCompTrader = this.GetComp<CompPowerTrader>();
            this.ticksUntilReady = 0;
        }

        public void Tick()
        {
            base.Tick();
            if (this.ticksUntilReady > 0)
            {
                this.ticksUntilReady--;
            }
        }

        protected override void SpringSub(Pawn p)
        {
            if (p != null && this.powerCompTrader != null && this.powerCompTrader.PowerOn && this.ticksUntilReady == 0)
            {
                if (!p.HostileTo(this.Faction)) return; // Solo afecta a hostiles
                Log.Message($"FloorStunTrap triggered by {p.Name} at {this.Position}");
                DamageInfo dinfo = new DamageInfo(DefDatabase<DamageDef>.GetNamed("StunCustomDamage"), 1f, 0f, -1f, this);
                p.TakeDamage(dinfo);
                this.ticksUntilReady = CooldownTicks;
            }
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            if (this.ticksUntilReady > 0)
            {
                return baseString + "\n" + $"Recharging: {(this.ticksUntilReady / 60f):F1} seconds remaining";
            }
            return baseString + "\n" + "Ready to activate";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksUntilReady, "ticksUntilReady", 0);
        }
    }


    public class Building_FloorRayTrap : Building_Trap
    {
        private CompPowerTrader powerComp;
        private int rayAmmoCount = 0; // Initialize at 0 to force initial reload
        private const int MaxAmmo = 5;
        private const int BasePowerConsumption = 25; // Base power consumption in watts
        private const int AmmoReloadCost = 15; // Additional power consumption during reload
        private const int ReloadTimePerAmmo = 60; // 1 second per ammo (60 ticks = 1s)
        private const int WaitTimeAfterShot = 180; // 3 seconds after firing (180 ticks = 3s)
        private int reloadTimer = 0;
        private int waitTimer = 0;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = this.GetComp<CompPowerTrader>();
            if (powerComp != null)
            {
                powerComp.PowerOutput = -BasePowerConsumption; // Set initial power consumption
                if (!respawningAfterLoad)
                {
                    rayAmmoCount = 0; // Start with 0 ammo to force reload
                    reloadTimer = 0;
                    waitTimer = 0;
                }
            }
            else
            {
                Log.Error($"[SecurityEnergy] FloorRayTrap at {this.Position} failed to initialize CompPowerTrader.");
            }
        }

        public void Tick()
        {
            base.Tick();
            if (powerComp == null || !powerComp.PowerOn)
            {
                powerComp.PowerOutput = 0; // No power, no consumption
                reloadTimer = 0; // Reset reload timer when powered off
                return;
            }

            // Handle wait period after firing
            if (waitTimer > 0)
            {
                waitTimer--;
                powerComp.PowerOutput = -BasePowerConsumption;
                return;
            }

            // Recharge logic
            if (rayAmmoCount < MaxAmmo)
            {
                // Immediate recharge if ammo is 0
                if (rayAmmoCount == 0)
                {
                    powerComp.PowerOutput = -(BasePowerConsumption + AmmoReloadCost); // 40W during recharge
                    reloadTimer++;
                    if (reloadTimer >= ReloadTimePerAmmo)
                    {
                        rayAmmoCount++;
                        reloadTimer = 0;
                        if (rayAmmoCount >= MaxAmmo)
                        {
                            powerComp.PowerOutput = -BasePowerConsumption; // Back to base consumption
                        }
                    }
                }
                // Partial ammo: wait 5 seconds (300 ticks) before recharging
                else
                {
                    if (waitTimer <= 0)
                    {
                        powerComp.PowerOutput = -(BasePowerConsumption + AmmoReloadCost);
                        reloadTimer++;
                        if (reloadTimer >= ReloadTimePerAmmo)
                        {
                            rayAmmoCount++;
                            reloadTimer = 0;
                            if (rayAmmoCount >= MaxAmmo)
                            {
                                powerComp.PowerOutput = -BasePowerConsumption;
                            }
                        }
                    }
                }
            }
            else
            {
                // Ammo full, maintain base consumption
                powerComp.PowerOutput = -BasePowerConsumption;
                reloadTimer = 0;
            }
        }

        protected override void SpringSub(Pawn p)
        {
            if (p == null || p.Dead || !p.HostileTo(this.Faction) || powerComp == null || !powerComp.PowerOn || rayAmmoCount <= 0)
            {
                if (p != null && powerComp != null)
                {
                    Log.Message($"[SecurityEnergy] FloorRayTrap at {this.Position} failed to activate: " +
                                $"Pawn: {p.Name}, Dead: {p.Dead}, Hostile: {p.HostileTo(this.Faction)}, " +
                                $"PowerOn: {powerComp.PowerOn}, Ammo: {rayAmmoCount}");
                }
                return;
            }

            // Apply stun effect directly
            DamageInfo dinfo = new DamageInfo(DefDatabase<DamageDef>.GetNamed("StunCustomDamage", false), 1f, 0f, -1f, this);
            if (dinfo.Def != null)
            {
                p.TakeDamage(dinfo);
                Log.Message($"[SecurityEnergy] FloorRayTrap at {this.Position} stunned {p.Name}");
            }
            else
            {
                Log.Error($"[SecurityEnergy] StunCustomDamage not found in DefDatabase.");
            }

            // Apply optional EMP effect
            if (Rand.Chance(0.03f))
            {
                DamageInfo empDinfo = new DamageInfo(DamageDefOf.EMP, 1f, 0f, -1f, this);
                p.TakeDamage(empDinfo);
                Log.Message($"[SecurityEnergy] FloorRayTrap at {this.Position} applied EMP to {p.Name}");
            }

            // Visual and sound effects
            if (this.Map != null)
            {
                MoteMaker.MakeStaticMote(this.Position, this.Map, ThingDef.Named("Mote_BeamRepeaterLaser"), 1f);
                SoundDefOf.Crunch?.PlayOneShot(new TargetInfo(this.Position, this.Map));
            }

            // Decrease ammo and set wait timer
            rayAmmoCount--;
            waitTimer = WaitTimeAfterShot;

            // Reset reload timer if ammo is depleted
            if (rayAmmoCount == 0)
            {
                reloadTimer = 0;
            }
        }
        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            string ammoStatus = $"Ray Ammo: {rayAmmoCount}/{MaxAmmo}";
            string reloadStatus;
            if (powerComp == null || !powerComp.PowerOn)
            {
                reloadStatus = "No Power";
            }
            else if (waitTimer > 0)
            {
                reloadStatus = $"Waiting: {(waitTimer / 60f):F1}s remaining";
            }
            else if (rayAmmoCount < MaxAmmo && reloadTimer > 0)
            {
                float reloadPercent = (reloadTimer / (float)ReloadTimePerAmmo) * 100f;
                reloadStatus = $"Charging: {reloadPercent:F0}% (Ammo {rayAmmoCount + 1}/{MaxAmmo})";
            }
            else
            {
                reloadStatus = rayAmmoCount == MaxAmmo ? "Fully Charged" : "Ready to Recharge";
            }
            return $"{baseString}\n{ammoStatus}\n{reloadStatus}";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref rayAmmoCount, "rayAmmoCount", 0);
            Scribe_Values.Look(ref reloadTimer, "reloadTimer", 0);
            Scribe_Values.Look(ref waitTimer, "waitTimer", 0);
        }
    }
}
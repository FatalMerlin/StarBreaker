using System.Text;
using StarBreaker.Common;
using StarBreaker.DataCore;
using StarBreaker.Extraction;
using StarBreaker.P4k;
using StarBreaker.Sandbox.Generated;

namespace StarBreaker.Sandbox;

public static class GeneratedSandbox
{
    public static void Run()
    {
        var timer = new TimeLogger();

        var p4kFile = P4kFile.FromFile(SandboxPaths.P4kPath);
        var p4k = P4kDirectoryNode.FromP4k(p4kFile);
        var dcbStream = p4k.OpenRead(@"Data\Game2.dcb");
        timer.LogReset("Loaded P4K");

        var db = new DataCoreDatabase(dcbStream);
        var dataCore = new DataCoreBinary(db);
        var reader = dataCore.Reader;
        timer.LogReset("Created DataCoreBinary");

        var allRecords = db.MainRecords
            .AsParallel()
            .Select(id => reader.GetFromMainRecord(db.GetRecord(id)))
            .ToList();
        timer.LogReset($"Loaded {allRecords.Count} records");

        // Find all entities with a weapon component
        var weapons = allRecords
            .Where(r => r.Data is EntityClassDefinition)
            .Select(r => (r.FileName, r.Id, Entity: (EntityClassDefinition)r.Data))
            .Where(x => x.Entity.Components.Any(c => c?.Value is SCItemWeaponComponentParams))
            .OrderBy(x => x.FileName)
            .ToList();

        timer.LogReset($"Found {weapons.Count} weapons");

        var outputPath = Path.Combine(SandboxPaths.ResearchFolder, "ship_weapons.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine($"Star Citizen Ship Weapons Database — {weapons.Count} weapons");
        writer.WriteLine(new string('=', 120));
        writer.WriteLine();

        foreach (var (fileName, recordId, entity) in weapons)
        {
            var weaponComp = entity.Components.Select(c => c?.Value).OfType<SCItemWeaponComponentParams>().First();
            var attachComp = entity.Components.Select(c => c?.Value).OfType<SAttachableComponentParams>().FirstOrDefault();

            var name = Path.GetFileNameWithoutExtension(fileName);
            var displayName = attachComp?.AttachDef.Localization.Name ?? "";
            var itemType = attachComp?.AttachDef.Type.ToString() ?? "?";
            var itemSubType = attachComp?.AttachDef.SubType.ToString() ?? "?";
            var size = attachComp?.AttachDef.Size ?? 0;
            var grade = attachComp?.AttachDef.Grade ?? 0;
            var tags = attachComp?.AttachDef.Tags ?? "";
            var manufacturer = attachComp?.AttachDef.Manufacturer?.Value;
            var mfrName = manufacturer?.Localization.Name ?? "";

            writer.WriteLine($"--- {name} ---");
            writer.WriteLine($"  ID:           {recordId}");
            writer.WriteLine($"  Display Name: {displayName}");
            writer.WriteLine($"  Manufacturer: {mfrName}");
            writer.WriteLine($"  Type:         {itemType} / {itemSubType}");
            writer.WriteLine($"  Size:         {size}   Grade: {grade}");
            writer.WriteLine($"  Tags:         {tags}");
            writer.WriteLine($"  Geometry Tags:{weaponComp.geometryTags}");

            // Helper to write launcher (SProjectileLauncher) data
            void WriteLauncherData(string indent, DataCoreRef<SLauncherBase>? launchRef)
            {
                if (launchRef?.Value is SProjectileLauncher proj)
                {
                    var parts = new List<string>();
                    if (proj.pelletCount > 1) parts.Add($"pellets={proj.pelletCount}");
                    if (proj.ammoCost != 1) parts.Add($"ammoCost={proj.ammoCost}");
                    if (proj.damageMultiplier != 1f) parts.Add($"dmgMul={proj.damageMultiplier:F2}");
                    var sp = proj.spreadParams;
                    if (sp.max > 0) parts.Add($"spread={sp.min:F1}-{sp.max:F1}  attack={sp.attack:F1}  decay={sp.decay:F1}");
                    if (parts.Count > 0)
                        writer.WriteLine($"{indent}Launcher:  {string.Join("   ", parts)}");
                }
            }

            // Helper to write a single fire action's details
            void WriteFireAction(string indent, SWeaponActionParams fireAction)
            {
                var typeName = fireAction.GetType().Name.Replace("SWeaponActionFire", "").Replace("Params", "");
                writer.Write($"{indent}Fire Mode:    {fireAction.name} ({typeName})");
                writer.WriteLine($"  aiMode={fireAction.aiShootingMode}");

                switch (fireAction)
                {
                    case SWeaponActionFireSingleParams single:
                        writer.WriteLine($"{indent}  fireRate={single.fireRate} rpm   heatPerShot={single.heatPerShot:F2}   wearPerShot={single.wearPerShot:F4}");
                        WriteLauncherData(indent + "  ", single.launchParams);
                        break;
                    case SWeaponActionFireRapidParams rapid:
                        writer.WriteLine($"{indent}  fireRate={rapid.fireRate} rpm   heatPerShot={rapid.heatPerShot:F2}   wearPerShot={rapid.wearPerShot:F4}");
                        writer.WriteLine($"{indent}  spinUp={rapid.spinUpTime:F2}s   spinDown={rapid.spinDownTime:F2}s   fireDuringSpinUp={rapid.fireDuringSpinUp}");
                        WriteLauncherData(indent + "  ", rapid.launchParams);
                        break;
                    case SWeaponActionFireBurstParams burst:
                        writer.WriteLine($"{indent}  fireRate={burst.fireRate} rpm   shotCount={burst.shotCount}   cooldown={burst.cooldownTime:F2}s");
                        writer.WriteLine($"{indent}  heatPerShot={burst.heatPerShot:F2}   wearPerShot={burst.wearPerShot:F4}");
                        WriteLauncherData(indent + "  ", burst.launchParams);
                        break;
                    case SWeaponActionFireChargedParams charged:
                        writer.WriteLine($"{indent}  chargeTime={charged.chargeTime:F2}s   overchargeTime={charged.overchargeTime:F2}s   cooldown={charged.cooldownTime:F2}s");
                        var mod = charged.maxChargeModifier;
                        writer.WriteLine($"{indent}  maxChargeMod: dmgMul={mod.damageMultiplier:F2}  fireRateMul={mod.fireRateMultiplier:F2}  projSpeedMul={mod.projectileSpeedMultiplier:F2}  pellets={mod.pellets}");
                        var innerAction = charged.weaponAction?.Value;
                        if (innerAction != null)
                            WriteFireAction(indent + "  [Inner] ", innerAction);
                        break;
                    case SWeaponActionFireBeamParams beam:
                        writer.WriteLine($"{indent}  fullDmgRange={beam.fullDamageRange:F0}m   zeroDmgRange={beam.zeroDamageRange:F0}m   hitRadius={beam.hitRadius:F2}");
                        writer.WriteLine($"{indent}  heatPerSec={beam.heatPerSecond:F2}   energyDraw={beam.minEnergyDraw:F0}-{beam.maxEnergyDraw:F0}");
                        writer.WriteLine($"{indent}  chargeUp={beam.chargeUpTime:F2}s   chargeDown={beam.chargeDownTime:F2}s");
                        var beamDmg = beam.damagePerSecond?.Value;
                        if (beamDmg is DamageInfo beamDi)
                            writer.WriteLine($"{indent}  DPS: phys={beamDi.DamagePhysical:F1} energy={beamDi.DamageEnergy:F1} distortion={beamDi.DamageDistortion:F1} thermal={beamDi.DamageThermal:F1} bio={beamDi.DamageBiochemical:F1} stun={beamDi.DamageStun:F1}");
                        break;
                    case SWeaponActionSequenceParams sequence:
                        writer.WriteLine($"{indent}  mode={sequence.mode}   entries={sequence.sequenceEntries.Length}");
                        foreach (var entry in sequence.sequenceEntries)
                        {
                            var inner = entry.weaponAction?.Value;
                            if (inner == null) continue;
                            var delayStr = entry.delay > 0 ? $"delay={entry.delay:F2}{entry.unit} " : "";
                            var repStr = entry.repetitions > 1 ? $"x{entry.repetitions} " : "";
                            writer.Write($"{indent}  [{delayStr}{repStr}] ");
                            WriteFireAction(indent + "    ", inner);
                        }
                        break;
                }
            }

            // Fire actions
            foreach (var fireActionRef in weaponComp.fireActions)
            {
                var fireAction = fireActionRef?.Value;
                if (fireAction == null) continue;
                WriteFireAction("  ", fireAction);
            }

            // Ammo container traversal
            var ammoContainerEntity = weaponComp.ammoContainerRecord?.Value;
            if (ammoContainerEntity != null)
            {
                var ammoContainer = ammoContainerEntity.Components
                    .Select(c => c?.Value)
                    .OfType<SAmmoContainerComponentParams>()
                    .FirstOrDefault();

                if (ammoContainer != null)
                {
                    writer.WriteLine($"  Ammo:         max={ammoContainer.maxAmmoCount}   initial={ammoContainer.initialAmmoCount}   type={ammoContainer.ammoContainerType}");

                    var ammoParams = ammoContainer.ammoParamsRecord?.Value;
                    if (ammoParams != null)
                    {
                        writer.WriteLine($"  Projectile:   speed={ammoParams.speed:F0} m/s   lifetime={ammoParams.lifetime:F2}s   range~{ammoParams.speed * ammoParams.lifetime:F0}m");
                        writer.WriteLine($"                size={ammoParams.size}   category={ammoParams.ammoCategory}   inheritVelocity={ammoParams.inheritVelocity:F2}");

                        // Traverse into BulletProjectileParams for damage
                        var projParams = ammoParams.projectileParams?.Value;
                        if (projParams is BulletProjectileParams bullet)
                        {
                            var dmgBase = bullet.damage?.Value;
                            if (dmgBase is DamageInfo di)
                            {
                                writer.WriteLine($"  Damage/hit:   phys={di.DamagePhysical:F1}  energy={di.DamageEnergy:F1}  distortion={di.DamageDistortion:F1}  thermal={di.DamageThermal:F1}  bio={di.DamageBiochemical:F1}  stun={di.DamageStun:F1}");

                                // Compute total damage per hit and DPS
                                var totalPerHit = di.DamagePhysical + di.DamageEnergy + di.DamageDistortion + di.DamageThermal + di.DamageBiochemical + di.DamageStun;
                                // Get fire rate from first fire action
                                var firstFireRate = weaponComp.fireActions
                                    .Select(fa => fa?.Value)
                                    .Where(fa => fa != null)
                                    .Select(fa => fa switch
                                    {
                                        SWeaponActionFireSingleParams s => s.fireRate,
                                        SWeaponActionFireRapidParams r => r.fireRate,
                                        SWeaponActionFireBurstParams b => b.fireRate,
                                        _ => 0
                                    })
                                    .FirstOrDefault(r => r > 0);

                                if (firstFireRate > 0)
                                {
                                    var dps = totalPerHit * firstFireRate / 60.0;
                                    writer.WriteLine($"  DPS (raw):    {dps:F1} ({totalPerHit:F1} x {firstFireRate} rpm / 60)");
                                }
                            }
                            else if (dmgBase is DamageParams dp)
                            {
                                var macro = dp.damageMacro?.Value;
                                if (macro != null)
                                {
                                    var di2 = macro.damageInfo;
                                    writer.WriteLine($"  Damage/hit:   phys={di2.DamagePhysical:F1}  energy={di2.DamageEnergy:F1}  distortion={di2.DamageDistortion:F1}  thermal={di2.DamageThermal:F1}  (via macro, total={dp.damageTotal:F1})");
                                }
                                else
                                {
                                    writer.WriteLine($"  Damage/hit:   total={dp.damageTotal:F1} (macro ref)");
                                }
                            }

                            if (bullet.impactRadius > 0)
                                writer.WriteLine($"  Impact:       radius={bullet.impactRadius:F2}  minRadius={bullet.minImpactRadius:F2}");

                            var pierceability = bullet.pierceabilityParams;
                            if (pierceability.maxPenetrationThickness > 0)
                                writer.WriteLine($"  Pierce:       falloff L1={pierceability.damageFalloffLevel1:F2}  L2={pierceability.damageFalloffLevel2:F2}  L3={pierceability.damageFalloffLevel3:F2}  maxThickness={pierceability.maxPenetrationThickness:F2}");

                            // Detonation / explosion info
                            var detonation = bullet.detonationParams?.Value;
                            if (detonation != null)
                            {
                                writer.Write($"  Detonation:   armTime={detonation.armTime:F2}s");
                                if (detonation.explodeOnImpact) writer.Write("  [explodeOnImpact]");
                                if (detonation.explodeOnExpire) writer.Write("  [explodeOnExpire]");
                                writer.WriteLine();

                                var explosion = detonation.explosionParams;
                                if (explosion.maxRadius > 0)
                                {
                                    writer.WriteLine($"  Explosion:    minR={explosion.minRadius:F1}  maxR={explosion.maxRadius:F1}  pressure={explosion.pressure:F1}");
                                    var explDmg = explosion.damage?.Value;
                                    if (explDmg is DamageInfo edi)
                                        writer.WriteLine($"  Expl Damage:  phys={edi.DamagePhysical:F1}  energy={edi.DamageEnergy:F1}  distortion={edi.DamageDistortion:F1}  thermal={edi.DamageThermal:F1}");
                                }
                            }

                            // Proximity trigger
                            var prox = bullet.proximityTriggerParams?.Value;
                            if (prox != null)
                                writer.WriteLine($"  Proximity:    radius={prox.radius:F1}m  armTime={prox.armTime:F2}s");
                        }
                    }
                }
            }
            else if (!weaponComp.ShouldIgnorePrimaryAmmoContainer)
            {
                writer.WriteLine($"  Ammo:         (no ammo container linked)");
            }

            writer.WriteLine();
        }

        writer.Flush();
        timer.LogReset($"Wrote {weapons.Count} weapons to {outputPath}");

        Console.WriteLine($"Wrote {weapons.Count} weapon entries to:\n  {outputPath}");
        timer.LogReset("Done");
    }
}

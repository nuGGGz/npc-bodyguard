using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Oxide.Plugins;
using Oxide.Core;

namespace Carbon.Plugins
{
    [Info("NPC Bodyguard", "nuGGGz", "0.0.1")]
    [Description("Spawns a personal NPC that follows you and attacks nearby players and other NPCs.")]
    public class NPCBodyguard : CarbonPlugin
    {
        // Plugin References - Required
        [PluginReference]
        private Plugin NpcSpawn;

        // Data Files
        private StoredData Data;
        private ConfigData config;

        // Initialization
        private void OnServerInitialized(bool serverInitialized)
        {
            // Check if the NpcSpawn plugin is installed - Instead of using // Requires : NpcSpawn
            if (!plugins.Exists("NpcSpawn"))
            {
                PrintError("You must have NpcSpawn installed");
                NextTick(() => Interface.Oxide.UnloadPlugin(Name));
                return;
            }

            // Loads the configuration file
            LoadConfigData();
        }
        private void Init()
        {
            // Loads the data
            Data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("NPCBodyguard");
            Puts("Loaded " + Data.Bodyguards.Count + " Bodyguards");

            // Registers permissions
            permission.RegisterPermission(PERMISSION_USE, this);
            permission.RegisterPermission(PERMISSION_MAX, this);
            permission.RegisterPermission(PERMISSION_COOL, this);
        }
        private void Unload()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, Data);
        }

        #region Configuration
        public static readonly string PERMISSION_USE = "npcbodyguard.use";
        public static readonly string PERMISSION_MAX = "npcbodyguard.maxatonce";
        public static readonly string PERMISSION_COOL = "npcbodyguard.cooldown";
        protected override void LoadDefaultConfig()
        {
            Puts("Generating default config...");
            config = new ConfigData();
            SaveConfigData();
        }
        private void LoadConfigData()
        {
            try
            {
                config = Config.ReadObject<ConfigData>();
            }
            catch
            {
                config = new ConfigData();
            }
        }
        private void SaveConfigData()
        {
            Config.WriteObject(config, true);
        }
        public class ConfigData
        {
            [JsonProperty("Friendly Fire is on?")]
            public bool friendlyFire = true;
        }
        #endregion

        #region Bodyguard
        // Bodyguard class
        private class Bodyguard
        {
            public ulong PlayerID { get; set; }
            public ulong OwnerID { get; set; }
        }

        #endregion

        #region Commands
        [Command("bodyguard")]
        private void CMD_bodyguard(BasePlayer player, string cmd, string[] args)
        {
            
            if (args.Length == 0) // Send the player information
            {
                player.ChatMessage("<color=#5EFF59>NPC Bodyguard</color>\n\nSpawn Bodyguard - <color=#ffd479>/bodyguard spawn</color>");
                return;
            }
            else if (args[0] == "spawn") // Spawn a bodyguard
            {
                // Check if the player has permission to spawn a bodyguard
                if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
                {
                    player.ChatMessage("<color=#5EFF59>NPC Bodyguard</color>\n\nYou do not have permission to spawn a bodyguard.");
                    return;
                }

                // Spawn the bodyguard
                generateNPCFor(player);
            }
        }

        #endregion

        #region Functions
        private void generateNPCFor(BasePlayer player)
        {
            // Get the player's position
            Vector3 pos = player.transform.position;

            // Spawn the NPC
            ScientistNPC npc = (ScientistNPC)NpcSpawn.Call("SpawnNpc", pos, GetObjectConfig(npcPreset, pos));
            if (npc == null)
            {
                Puts("Failed to spawn NPC");
                return;
            }

            // Mark the NPC as a bodyguard for the player
            NpcSpawn.Call("AddTargetGuard", npc, player);

            // Set the NPC's owner to the player
            npc.Brain.OwningPlayer = player;
            npc.Brain.SetPetOwner(player);

            Puts("NPC " + npc.userID + " spawned for " + player.userID);

            // Add the NPC to the list of Bodyguards
            Data.Bodyguards.Add(new Bodyguard { PlayerID = npc.userID, OwnerID = player.userID });
        }
        
        private bool IsBodyguard(BasePlayer target)
        {
            return Data.Bodyguards.Any(x => x.PlayerID == target.userID);
        }

        private bool IsPlayer(BasePlayer player)
        {
            if (player == null) return false;

            return player.userID.IsSteamId();
        }
        
        private bool IsBetterNPC(BasePlayer player)
        {
            if (player == null) return false;
            if (player.skinID != 11162132011012) return false;

            return true;
        }
        #endregion

        #region Hooks
        private object? OnCustomNpcTarget(ScientistNPC attacker, BasePlayer target)
        {
            if (attacker == null || target == null) return null;

            bool shooterIsBodyguard = IsBodyguard(attacker);
            bool shooterIsBetterNPC = IsBetterNPC(attacker);

            bool targetIsBodyguard = IsBodyguard(target);
            bool targetIsPlayer = IsPlayer(target);
            bool targetIsBetterNPC = IsBetterNPC(target);

            // If the shooter is a Bodyguard, allow the target on anyone except the owner
            if (shooterIsBodyguard)
            {
                // If the target is the owner of the bodyguard, ignore the target
                if (attacker.Brain.OwningPlayer.userID == target.userID)
                {
                    return false;
                }

                // Allow the bodyguard to attack the target
                return true;
            }

            // If the target is a player, allow the bodyguard to attack
            if (targetIsPlayer)
            {
                return true;
            }

            // If the shooter is a BetterNPC, allow it to target the bodyguard
            if (shooterIsBetterNPC && targetIsBodyguard)
            {
                return true;
            }

            return null;
        }
        private object? OnNpcTarget(ScientistNPC attacker, BasePlayer target)
        {
            if (attacker == null || target == null) return null;

            bool targetIsBodyguard = IsBodyguard(target);

            // If the target is a Bodyguard, allow the in-game NPC to target it
            if (targetIsBodyguard)
            {
                return true;
            }

            return null;
        }
        private object OnEntityTakeDamage(ScientistNPC victim, HitInfo info)
        {
            if (victim == null || info == null) return null;

            bool victimIsBodyguard = IsBodyguard(victim);
            bool attackerIsBodyguard = IsBodyguard(info.Initiator.ToPlayer());
            bool victimisBetterNPC = IsBetterNPC(victim);
            bool attackerIsBetterNPC = IsBetterNPC(info.Initiator.ToPlayer());

            if ((attackerIsBodyguard && victimisBetterNPC) || (victimIsBodyguard && attackerIsBetterNPC))
            {
                Puts("Bodyguard vs BetterNPC");
                Puts("Health: " + victim.health + " | Damage: " + info.damageTypes.Total());
                Puts("Victim skinID: " + victim.skinID + " | Attacker skinID: " + info.Initiator.ToPlayer().skinID);

                victim.health = victim.health - info.damageTypes.Total();
            }

            return null;
        }
        private void OnEntityDeath(ScientistNPC victim, HitInfo info)
        {
            bool victimIsBodyguard = IsBodyguard(victim);
            if (victimIsBodyguard)
            {
                // Get the owner of the bodyguard
                BasePlayer owner = BasePlayer.FindByID(Data.Bodyguards.Find(x => x.PlayerID == victim.userID).OwnerID);

                // Send a message to the owner
                owner.ChatMessage("Your bodyguard has died.");

                // Remove the bodyguard from the list
                Data.Bodyguards.RemoveAll(x => x.PlayerID == victim.userID);
                
            }
        }
        #endregion

        #region NPC 
        NpcConfig npcPreset = new NpcConfig
        {
            Name = "Bodyguard",
            WearItems = new HashSet<NpcWear>
                {
                    new NpcWear { ShortName = "metal.facemask", SkinID = 0 },
                    new NpcWear { ShortName = "metal.plate.torso", SkinID = 0 },
                    new NpcWear { ShortName = "pants", SkinID = 0 },
                    new NpcWear { ShortName = "shoes.boots", SkinID = 0 },
                    new NpcWear { ShortName = "hoodie", SkinID = 0 }
                },
            BeltItems = new HashSet<NpcBelt>
                {
                    new NpcBelt { ShortName = "rifle.ak", Amount = 1, SkinID = 0, Mods = new HashSet<string>(), Ammo = "ammo.rifle" }
                },
            Health = 200f,
            RoamRange = 5f,
            ChaseRange = 10f,
            SenseRange = 50f,
            ListenRange = 25f,
            AttackRangeMultiplier = 2f,
            CheckVisionCone = true,
            VisionCone = 135f,
            HostileTargetsOnly = false,
            DamageScale = 2f,
            TurretDamageScale = 1f,
            AimConeScale = 2f,
            DisableRadio = true,
            CanRunAwayWater = false,
            CanSleep = false,
            SleepDistance = 0f,
            Speed = 8f,
            AreaMask = 1,
            AgentTypeID = -1372625422,
            HomePosition = string.Empty,
            MemoryDuration = 60f,
            States = new HashSet<string> { "RoamState", "ChaseState", "CombatState", "CombatStationaryState" }
        };
        private static JObject GetObjectConfig(NpcConfig config, Vector3 pos, int typeNavMesh = 0)
        {
            HashSet<string> states = new HashSet<string> { "RoamState", "ChaseState", "CombatState", "CombatStationaryState" };
            return new JObject
            {
                ["Name"] = "NPC Bodyguard",
                ["WearItems"] = new JArray { config.WearItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["SkinID"] = x.SkinID }) },
                ["BeltItems"] = new JArray { config.BeltItems.Select(x => new JObject { ["ShortName"] = x.ShortName, ["Amount"] = x.Amount, ["SkinID"] = x.SkinID, ["Mods"] = new JArray { x.Mods }, ["Ammo"] = x.Ammo }) },
                ["Health"] = config.Health,
                ["RoamRange"] = config.RoamRange,
                ["ChaseRange"] = config.ChaseRange,
                ["SenseRange"] = config.SenseRange,
                ["ListenRange"] = config.SenseRange / 2f,
                ["AttackRangeMultiplier"] = config.AttackRangeMultiplier,
                ["CheckVisionCone"] = config.CheckVisionCone,
                ["VisionCone"] = config.VisionCone,
                ["HostileTargetsOnly"] = false,
                ["DamageScale"] = config.DamageScale,
                ["TurretDamageScale"] = 1f,
                ["AimConeScale"] = config.AimConeScale,
                ["DisableRadio"] = config.DisableRadio,
                ["CanRunAwayWater"] = false,
                ["CanSleep"] = false,
                ["SleepDistance"] = 0f,
                ["Speed"] = config.Speed,
                ["AreaMask"] = typeNavMesh == 0 ? 1 : 25,
                ["AgentTypeID"] = typeNavMesh == 0 ? -1372625422 : 0,
                ["HomePosition"] = pos.ToString(),
                ["MemoryDuration"] = config.MemoryDuration,
                ["States"] = new JArray { states }
            };
        }

        internal class NpcConfig
        {
            public string Name { get; set; }
            public HashSet<NpcWear> WearItems { get; set; }
            public HashSet<NpcBelt> BeltItems { get; set; }
            public string Kit { get; set; }
            public float Health { get; set; }
            public float RoamRange { get; set; }
            public float ChaseRange { get; set; }
            public float SenseRange { get; set; }
            public float ListenRange { get; set; }
            public float AttackRangeMultiplier { get; set; }
            public bool CheckVisionCone { get; set; }
            public float VisionCone { get; set; }
            public bool HostileTargetsOnly { get; set; }
            public float DamageScale { get; set; }
            public float TurretDamageScale { get; set; }
            public float AimConeScale { get; set; }
            public bool DisableRadio { get; set; }
            public bool CanRunAwayWater { get; set; }
            public bool CanSleep { get; set; }
            public float SleepDistance { get; set; }
            public float Speed { get; set; }
            public int AreaMask { get; set; }
            public int AgentTypeID { get; set; }
            public string HomePosition { get; set; }
            public float MemoryDuration { get; set; }
            public HashSet<string> States { get; set; }
        }

        internal class NpcBelt { public string ShortName; public int Amount; public ulong SkinID; public HashSet<string> Mods; public string Ammo; }

        internal class NpcWear { public string ShortName; public ulong SkinID; }
        #endregion

        #region Data
        private class StoredData
        {
            public List<Bodyguard> Bodyguards = new List<Bodyguard>();

            public StoredData()
            {
            }
        }
        #endregion

    }

}


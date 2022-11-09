﻿using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using SpaceCraft;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FeatMultiplayer
{
    public partial class Plugin : BaseUnityPlugin
    {
        static ConfigEntry<int> port;
        static ConfigEntry<int> networkFrequency;
        static ConfigEntry<int> fullSyncDelay;
        static ConfigEntry<int> smallSyncDelay;

        static ConfigEntry<bool> hostMode;
        static ConfigEntry<bool> useUPnP;
        static ConfigEntry<string> hostServiceAddress;
        static ConfigEntry<string> hostAcceptName;
        static ConfigEntry<string> hostAcceptPassword;
        static ConfigEntry<string> hostColor;
        static ConfigEntry<int> hostLogLevel;
        static ConfigEntry<int> maxClients;

        // client side properties
        static ConfigEntry<string> hostAddress;
        static ConfigEntry<string> clientName;
        static ConfigEntry<string> clientPassword;
        static ConfigEntry<string> clientColor;
        static ConfigEntry<int> clientLogLevel;

        static ConfigEntry<int> fontSize;
        static ConfigEntry<bool> slowdownConsumption;
        internal static ConfigEntry<int> playerNameFontSize;
        internal static ConfigEntry<float> playerNameHeight;
        internal static ConfigEntry<float> positionHeightOffset;
        internal static ConfigEntry<float> positionInstantUpdateDistance;
        internal static ConfigEntry<float> positionLerpSpeed;
        internal static ConfigEntry<float> rotationLerpSpeed;
        static ConfigEntry<string> emoteKey;
        static InputAction emoteAction;

        internal static Texture2D astronautFront;
        internal static Texture2D astronautBack;

        internal static Texture2D astronautFrontHost;
        internal static Texture2D astronautBackHost;

        internal static readonly Dictionary<string, List<Sprite>> emoteSprites = new();

        static readonly object logLock = new object();

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin is loaded!");

            theLogger = Logger;

            port = Config.Bind("General", "Port", 22526, "The port where the host server is running.");
            fontSize = Config.Bind("General", "FontSize", 20, "The font size used");
            networkFrequency = Config.Bind("General", "Frequency", 20, "The frequency of checking the network for messages.");
            fullSyncDelay = Config.Bind("General", "SyncDelay", 3000, "Delay between full sync from the host to the client, in milliseconds");
            smallSyncDelay = Config.Bind("General", "SyncDelaySmall", 500, "Delay between small sync from the host to the client, in milliseconds");
            slowdownConsumption = Config.Bind("General", "SlowdownConsumption", false, "Slows down health/food/water consumption rate");
            positionHeightOffset = Config.Bind("General", "PositionHeightOffset", -0.09f, "Adjust the position of players relative to the ground.");
            positionInstantUpdateDistance = Config.Bind("General", "PositionInstantUpdateDistance", 2f, "How far a player's target position has to be for an instant update. Increase this value if players skip while moving.");
            positionLerpSpeed = Config.Bind("General", "PositionLerpSpeed", 7f, "How fast player positions catch up to positions received via network messages.");
            rotationLerpSpeed = Config.Bind("General", "RotationLerpSpeed", 15f, "How fast player rotations catch up to rotations received via network messages.");
            playerNameFontSize = Config.Bind("General", "PlayerNameFontSize", 20, "Font size used to display the player's names above their avatar.");
            playerNameHeight = Config.Bind("General", "PlayerNameHeight", 2.25f, "The height of the name above the players");
            emoteKey = Config.Bind("General", "EmoteKey", "G", "The key to bring up the emote wheel.");

            hostMode = Config.Bind("Host", "Host", false, "If true, loading a save will also host it as a multiplayer game.");
            useUPnP = Config.Bind("Host", "UseUPnP", false, "If behind NAT, use UPnP to manually map the HostPort to the external IP address?");
            hostAcceptName = Config.Bind("Host", "Name", "Buddy,Dude", "Comma separated list of client names the host will accept.");
            hostAcceptPassword = Config.Bind("Host", "Password", "password,wordpass", "Comma separated list of the plaintext(!) passwords accepted by the host, in pair with the Host/Name list.");
            hostColor = Config.Bind("Host", "Color", "1,1,1,1", "The color of the host avatar as comma-separated RGBA floats");
            hostServiceAddress = Config.Bind("Host", "ServiceAddress", "default", "The local IP address the host would listen, '' for auto address, 'default' for first IPv4 local address, 'defaultv6' for first IPv6 local address");
            hostLogLevel = Config.Bind("Host", "LogLevel", 2, "0 - debug+, 1 - info+, 2 - warning+, 3 - error");
            maxClients = Config.Bind("Host", "MaxClients", 4, "Number of clients that can join at a time");

            hostAddress = Config.Bind("Client", "HostAddress", "", "The IP address where the Host can be located from the client.");
            clientName = Config.Bind("Client", "Name", "Buddy,Dude", "The list of client names to join with.");
            clientPassword = Config.Bind("Client", "Password", "password,wordpass", "The plaintext(!) password presented to the host when joining their game.");
            clientColor = Config.Bind("Client", "Color", "0.75,0.75,1,1", "The color of the client avatar as comma-separated RGBA floats");
            clientLogLevel = Config.Bind("Client", "LogLevel", 2, "0 - debug+, 1 - info+, 2 - warning+, 3 - error");

            Assembly me = Assembly.GetExecutingAssembly();
            string dir = Path.GetDirectoryName(me.Location);

            astronautFront = LoadPNG(Path.Combine(dir, "Astronaut_Front.png"));
            astronautBack = LoadPNG(Path.Combine(dir, "Astronaut_Back.png"));

            astronautFrontHost = LoadPNG(Path.Combine(dir, "Astronaut_Front_Host.png"));
            astronautBackHost = LoadPNG(Path.Combine(dir, "Astronaut_Back_Host.png"));

            InitReflectiveAccessors();
            
            TryInstallMachineOverrides();

            ApiSetup();

            EmoteSetup();

            Harmony.CreateAndPatchAll(typeof(Plugin));
        }

        static Texture2D LoadPNG(string filename)
        {
            Texture2D tex = new Texture2D(100, 200);
            tex.LoadImage(File.ReadAllBytes(filename));

            return tex;
        }

        static FieldInfo worldUnitCurrentTotalValue;
        static FieldInfo worldUnitsPositioningWorldUnitsHandler;
        static FieldInfo worldUnitsPositioningHasMadeFirstInit;
        static FieldInfo playerMultitoolCanUseLight;
        static FieldInfo worldObjectTextWorldObject;
        static FieldInfo worldObjectColorWorldObject;
        static MethodInfo sectorSceneLoaded;
        static MethodInfo actionSendInSpaceHandleRocketMultiplier;
        static FieldInfo machineGrowerIfLinkedGroupHasEnergy;
        static FieldInfo machineGrowerIfLinkedGroupWorldObject;
        static MethodInfo machineGrowerIfLinkedGroupSetInteractiveStatus;
        static FieldInfo uiWindowGroupSelectorWorldObject;
        /// <summary>
        /// PlayerEquipment.hasCleanConstructionChip
        /// </summary>
        static FieldInfo playerEquipmentHasCleanConstructionChip;

        static void InitReflectiveAccessors()
        {
            worldUnitCurrentTotalValue = AccessTools.Field(typeof(WorldUnit), "currentTotalValue");
            worldUnitsPositioningWorldUnitsHandler = AccessTools.Field(typeof(WorldUnitPositioning), "worldUnitsHandler");
            worldUnitsPositioningHasMadeFirstInit = AccessTools.Field(typeof(WorldUnitPositioning), "hasMadeFirstInit");
            playerMultitoolCanUseLight = AccessTools.Field(typeof(PlayerMultitool), "canUseLight");
            worldObjectTextWorldObject = AccessTools.Field(typeof(WorldObjectText), "worldObject");
            worldObjectColorWorldObject = AccessTools.Field(typeof(WorldObjectColor), "worldObject");

            sectorSceneLoaded = AccessTools.Method(typeof(Sector), "SceneLoaded", new Type[] { typeof(AsyncOperation) });

            actionSendInSpaceHandleRocketMultiplier = AccessTools.Method(typeof(ActionSendInSpace), "HandleRocketMultiplier", new Type[] { typeof(WorldObject) });

            machineGrowerIfLinkedGroupHasEnergy = AccessTools.Field(typeof(MachineGrowerIfLinkedGroup), "hasEnergy");
            machineGrowerIfLinkedGroupWorldObject = AccessTools.Field(typeof(MachineGrowerIfLinkedGroup), "worldObject"); ;
            machineGrowerIfLinkedGroupSetInteractiveStatus = AccessTools.Method(typeof(MachineGrowerIfLinkedGroup), "SetInteractiveStatus", new Type[] { typeof(bool), typeof(bool) });

            if (Chainloader.PluginInfos.TryGetValue(modCheatInventoryStackingGuid, out BepInEx.PluginInfo pi))
            {
                MethodInfo mi = AccessTools.Method(pi.Instance.GetType(), "GetStackCount", new Type[] { typeof(List<WorldObject>) });
                getStackCount = AccessTools.MethodDelegate<Func<List<WorldObject>, int>>(mi, null);

                stackSize = (ConfigEntry<int>)AccessTools.Field(pi.Instance.GetType(), "stackSize").GetValue(null);

                var getMultiplayerModeField = AccessTools.Field(pi.Instance.GetType(), "getMultiplayerMode");
                getMultiplayerModeField.SetValue(pi.Instance, new Func<string>(GetMultiplayerMode));
            }

            uiWindowGroupSelectorWorldObject = AccessTools.Field(typeof(UiWindowGroupSelector), "worldObject");

            playerEquipmentHasCleanConstructionChip = AccessTools.Field(typeof(PlayerEquipment), "hasCleanConstructionChip");

        }

    }
}

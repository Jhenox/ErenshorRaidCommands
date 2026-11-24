using System;
using System.Collections;
using UnityEngine.SceneManagement;
using HarmonyLib;
using Lunaris;
using UnityEngine;

namespace ErenshorRaidCommands
{
    internal static class PluginIdentity
    {
        public const string Guid = "com.github.Jhenox.erenshorraidcommands";
        public const string Version = "0.4.0";
    }

    [LunarisPlugin("Erenshor Raid Commands", PluginIdentity.Version, "Jhenox", "Manage raid members by name")]
    [LunarisPermission(LunarisPermission.Harmony)]
    public class Plugin : LunarisPlugin
    {
        private Harmony harmony;
        private bool pendingInvite;
        private bool batchRunning;
        internal static Plugin Instance;

        private void Awake()
        {
            if (Instance != null)
            {
                Logging.LogWarning("Duplicate Raid Commands plugin detected.");
                enabled = false;
                return;
            }

            Instance = this;
            try
            {
                harmony = new Harmony(PluginIdentity.Guid);
                harmony.PatchAll(typeof(Plugin).Assembly);
                Logging.LogInfo("Erenshor Raid Commands v" + PluginIdentity.Version + " loaded.");
            }
            catch (Exception ex)
            {
                Logging.LogError("Raid Commands initialization failed: " + ex);
                Cleanup();
                enabled = false;
            }
        }

        private void OnDestroy() { StopAllCoroutines(); Cleanup(); }

        private void Cleanup()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
                harmony = null;
            }
            pendingInvite = false;
            batchRunning = false;
            if (Instance == this) Instance = null;
        }

        internal static void Log(string message, string color = "yellow")
        {
            UpdateSocialLog.LogAdd(new ChatLogLine(
                "Raid Commands: " + message, ChatLogLine.LogType.SystemMessages, color));
        }

        internal void RunCommand(bool add, string name)
        {
            try
            {
                RaidManager raid = GameData.RaidManager;
                if (raid == null || raid.Raiders == null || GameData.SimMngr == null)
                {
                    Log("Raid or SimPlayer manager is not ready.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    Log(add ? "Usage: /raidadd <SimPlayer name>" : "Usage: /raidremove <SimPlayer name>");
                    return;
                }

                if (!GameData.RaidActive && add)
                {
                    if (!StartRaid()) return;
                }
                if (!GameData.RaidActive)
                {
                    Log("No active raid. Use /raidstart first.");
                    return;
                }
                RaidMemberSlot existing = FindRaidSlot(raid, name);
                if (!add)
                {
                    if (existing == null)
                    {
                        Log(Canonical(name) + " is not in your raid.");
                        return;
                    }
                    if (existing.AssignedAvatar == null)
                    {
                        Log(Canonical(name) + " has no active raid avatar; removal skipped.");
                        return;
                    }

                    string actualName = existing.AssignedSimTracking.SimName;
                    RaidMemberSlot previous = raid.SelectedSlot;
                    try
                    {
                        raid.SelectSlot(existing);
                        raid.DismissRaider();
                    }
                    finally
                    {
                        if (previous != null && previous != existing)
                            raid.SelectSlot(previous);
                    }
                    if (existing.AssignedSimTracking == null)
                        Log("Removed " + actualName + " from the raid.", "green");
                    else
                        Log("Could not confirm removal of " + name + ".");
                    return;
                }

                if (existing != null)
                {
                    Log(Canonical(name) + " is already in your raid.");
                    return;
                }

                bool hasSpace = false;
                foreach (RaidMemberSlot slot in raid.Raiders)
                {
                    if (slot != null && slot.AssignedAvatar == null &&
                        (slot.AssignedSimTracking == null ||
                         string.IsNullOrEmpty(slot.AssignedSimTracking.SimName)))
                    {
                        hasSpace = true;
                        break;
                    }
                }
                if (!hasSpace)
                {
                    Log("Raid is full; use /raidreplace <existing name> <new name> or /raidremove.");
                    return;
                }

                SimPlayer sim = FindActiveSim(name);
                if (sim == null)
                {
                    bool known = false;
                    foreach (SimPlayerTracking record in GameData.SimMngr.Sims)
                    {
                        if (record != null && string.Equals(record.SimName, name,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            known = true;
                            break;
                        }
                    }
                    if (known && TrySummonAndInvite(name)) return;
                    Log(known ? Canonical(name) + " is not active and could not be summoned."
                              : "No SimPlayer named '" + name + "' was found.");
                    return;
                }
                if (sim.MySimTracking == null || sim.GetThisNPC() == null)
                {
                    Log(name + " is not ready to join the raid.");
                    return;
                }

                raid.AddToRoster(sim);
                if (FindRaidSlot(raid, sim.MySimTracking.SimName) != null)
                    Log("Added " + sim.MySimTracking.SimName + " to the raid.", "green");
                else
                    Log("Could not confirm that " + Canonical(name) + " joined the raid.");
            }
            catch (Exception ex)
            {
                Logging.LogError("Raid command failed: " + ex);
                Log("Command failed; see the Lunaris log for details.");
            }
        }

        private static bool IsEmpty(RaidMemberSlot slot)
        {
            return slot != null && slot.AssignedAvatar == null &&
                (slot.AssignedSimTracking == null ||
                 string.IsNullOrEmpty(slot.AssignedSimTracking.SimName));
        }

        internal void ShowStatus(bool list)
        {
            try
            {
                RaidManager raid = GameData.RaidManager;
                if (raid == null || raid.Raiders == null || raid.Raiders.Count == 0)
                {
                    Log("Raid roster is not initialized.");
                    return;
                }
                int occupied = 0;
                foreach (RaidMemberSlot slot in raid.Raiders)
                    if (slot != null && slot.AssignedSimTracking != null &&
                        !string.IsNullOrEmpty(slot.AssignedSimTracking.SimName)) occupied++;
                Log("Raid slots: " + occupied + "/" + raid.Raiders.Count +
                    "; available: " + (raid.Raiders.Count - occupied) + ".",
                    occupied == raid.Raiders.Count ? "yellow" : "green");
                if (list)
                {
                    int i = 0;
                    foreach (RaidMemberSlot slot in raid.Raiders)
                    {
                        i++;
                        if (slot != null && slot.AssignedSimTracking != null &&
                            !string.IsNullOrEmpty(slot.AssignedSimTracking.SimName))
                            Log("Slot " + (i + 1) + ": " + slot.AssignedSimTracking.SimName, "green");
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogError("Raid status failed: " + ex);
                Log("Status failed; see the Lunaris log.");
            }
        }

        internal void Replace(string outgoing, string incoming)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(outgoing) || string.IsNullOrWhiteSpace(incoming))
                {
                    Log("Usage: /raidreplace <existing name> <new name>");
                    return;
                }
                RaidManager raid = GameData.RaidManager;
                if (raid == null || raid.Raiders == null || GameData.SimMngr == null)
                {
                    Log("Raid or SimPlayer manager is not ready.");
                    return;
                }
                if (!GameData.RaidActive) { Log("No active raid."); return; }
                RaidMemberSlot slot = FindRaidSlot(raid, outgoing);
                if (slot == null || slot.AssignedAvatar == null)
                {
                    Log(outgoing + " is not an active raid member.");
                    return;
                }
                if (FindRaidSlot(raid, incoming) != null)
                {
                    Log(incoming + " is already in your raid.");
                    return;
                }
                SimPlayer newcomer = FindActiveSim(incoming);
                if (newcomer == null || newcomer.MySimTracking == null || newcomer.GetThisNPC() == null)
                {
                    Log(incoming + " is not an active, available SimPlayer in this zone.");
                    return;
                }
                SimPlayer original = slot.AssignedAvatar;
                RaidMemberSlot previous = raid.SelectedSlot;
                try
                {
                    raid.SelectSlot(slot);
                    raid.DismissRaider();
                    if (!IsEmpty(slot))
                    {
                        Log("Could not clear " + outgoing + "'s slot; replacement canceled.");
                        return;
                    }
                    try
                    {
                        raid.AddToRosterAtSlot(newcomer, slot);
                    }
                    catch (Exception addEx)
                    {
                        Logging.LogError("Raid replacement add failed: " + addEx);
                    }
                    if (slot.AssignedSimTracking != null &&
                        string.Equals(slot.AssignedSimTracking.SimName, incoming,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Log("Replaced " + Canonical(outgoing) + " with " + newcomer.MySimTracking.SimName + " in the same slot.", "green");
                    }
                    else
                    {
                        if (IsEmpty(slot) && original != null)
                        {
                            try { raid.AddToRosterAtSlot(original, slot); }
                            catch (Exception restoreEx) { Logging.LogError("Raid rollback failed: " + restoreEx); }
                        }
                        Log("Replacement failed; " +
                            (FindRaidSlot(raid, outgoing) != null ? "original member restored." :
                             "original member could not be restored; check the raid roster."));
                    }
                }
                finally
                {
                    if (previous != null && previous != slot)
                        raid.SelectSlot(previous);
                }
            }
            catch (Exception ex)
            {
                Logging.LogError("Raid replacement failed: " + ex);
                Log("Replacement failed; inspect the raid roster and Lunaris log.");
            }
        }

        internal bool StartRaid()
        {
            try
            {
                if (GameData.RaidActive) { Log("Raid is already active."); return true; }
                if (GameData.GuildManager == null || GameData.RaidManager == null)
                { Log("Guild or raid manager is not ready."); return false; }
                // The game validates existing groups, zone, and guild restrictions.
                // summon:false avoids pulling the entire guild into the hall.
                GameData.GuildManager.CallToRaid(summon: false);
                if (!GameData.RaidActive)
                { Log("Raid could not be started; check the game's requirements."); return false; }
                Log("Raid started.", "green");
                return true;
            }
            catch (Exception ex)
            { Logging.LogError("Raid start failed: " + ex); Log("Raid start failed; see Lunaris log."); return false; }
        }

        internal void EndRaid()
        {
            try
            {
                if (!GameData.RaidActive) { Log("No active raid to end."); return; }
                if (GameData.GuildManager == null) { Log("Guild manager is not ready."); return; }
                StopAllCoroutines();
                pendingInvite = false;
                batchRunning = false;
                GameData.GuildManager.EndRaid();
                Log(GameData.RaidActive ? "Could not confirm raid termination." : "Raid ended.",
                    GameData.RaidActive ? "yellow" : "green");
            }
            catch (Exception ex)
            { Logging.LogError("Raid end failed: " + ex); Log("Raid end failed; see Lunaris log."); }
        }

        private bool TrySummonAndInvite(string name)
        {
            if (pendingInvite) { Log("Another summon/invite is already pending."); return true; }
            if (SceneManager.GetActiveScene().name != "Reliquary")
            { Log(name + " is not active. Individual summoning is restricted to Reliquary Hall."); return true; }
            if (GameData.GuildManager == null || GameData.PlayerControl == null ||
                GameData.ZoneAnnounce == null) return false;
            var guild = GameData.GuildManager.GetGuildDataByID(GameData.PlayerControl.MyGuild);
            if (guild == null) return false;
            bool member = false;
            foreach (string guildName in guild.GuildMembers)
                if (string.Equals(guildName, name, StringComparison.OrdinalIgnoreCase))
                { member = true; break; }
            if (!member) { Log(name + " is not a member of your guild; not summoning."); return true; }
            SimPlayerTracking tracking = null;
            foreach (SimPlayerTracking record in GameData.SimMngr.Sims)
                if (record != null && string.Equals(record.SimName, name, StringComparison.OrdinalIgnoreCase))
                { tracking = record; break; }
            if (tracking == null) return false;
            if (tracking.seekingPlayer || tracking.PersonalInvite || tracking.GoingToReliq)
            { Log(tracking.SimName + " is already traveling or unavailable; try again later."); return true; }
            if (tracking.CurScene == SceneManager.GetActiveScene().name)
            { Log(tracking.SimName + " is in this zone but has no active avatar; cannot safely summon."); return true; }
            tracking.GoingToReliq = true;
            pendingInvite = true;
            StartCoroutine(SummonAndInvite(tracking));
            Log("Summoning " + tracking.SimName + " to Reliquary Hall; invitation will follow when ready.");
            return true;
        }

        private IEnumerator SummonAndInvite(SimPlayerTracking tracking)
        {
            string name = tracking.SimName;
            try
            {
                // This coroutine uses the game's normal delayed spawning path (5-35 seconds).
                yield return new WaitForSeconds(5f);
                if (!GameData.RaidActive || SceneManager.GetActiveScene().name != "Reliquary")
                { Log("Summon canceled: raid ended or zone changed."); yield break; }
                GameData.SimMngr.SpawnMeInPlayerZone(
                    tracking, SceneManager.GetActiveScene().name,
                    GameData.ZoneAnnounce.transform.position, false);
                float deadline = Time.realtimeSinceStartup + 10f;
                while (FindActiveSim(name) == null && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (GameData.RaidActive && FindActiveSim(name) != null)
                    RunCommand(true, name);
                else Log("Summon completed, but " + Canonical(name) + " is not available for a raid invitation.");
            }
            finally { pendingInvite = false; }
        }

        private static string Canonical(string name)
        {
            if (GameData.SimMngr != null && GameData.SimMngr.Sims != null)
                foreach (SimPlayerTracking sim in GameData.SimMngr.Sims)
                    if (sim != null && string.Equals(sim.SimName, name, StringComparison.OrdinalIgnoreCase))
                        return sim.SimName;
            return name;
        }

        // Visible positions are 2..(Raiders.Count+1); position 1 is the player.
        private static RaidMemberSlot SlotAt(int visiblePosition)
        {
            RaidManager raid = GameData.RaidManager;
            if (raid == null || raid.Raiders == null || visiblePosition < 2 ||
                visiblePosition > raid.Raiders.Count + 1) return null;
            return raid.Raiders[visiblePosition - 2];
        }

        internal void RunRemoveTokens(string[] tokens)
        {
            if (!GameData.RaidActive) { Log("No active raid."); return; }
            RaidManager raid = GameData.RaidManager;
            if (raid == null || raid.Raiders == null) { Log("Raid manager is not ready."); return; }
            var targets = new System.Collections.Generic.List<string>();
            foreach (string token in tokens)
            {
                int hyphen = token.IndexOf('-');
                int first, last;
                if (hyphen > 0 && int.TryParse(token.Substring(0, hyphen), out first) &&
                    int.TryParse(token.Substring(hyphen + 1), out last))
                {
                    if (first > last || SlotAt(first) == null || SlotAt(last) == null)
                    { Log("Invalid raid position range: " + token); continue; }
                    for (int n = first; n <= last; n++)
                    {
                        RaidMemberSlot slot = SlotAt(n);
                        if (slot != null && slot.AssignedSimTracking != null)
                            targets.Add(slot.AssignedSimTracking.SimName);
                    }
                }
                else if (int.TryParse(token, out first))
                {
                    RaidMemberSlot slot = SlotAt(first);
                    if (slot == null) { Log("Invalid raid position: " + first); continue; }
                    if (slot.AssignedSimTracking == null) { Log("Position " + first + " is empty."); continue; }
                    targets.Add(slot.AssignedSimTracking.SimName);
                }
                else targets.Add(token);
            }
            var unique = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in targets)
                if (unique.Add(name)) RunCommand(false, name);
        }

        internal void RunAddTokens(string[] tokens)
        {
            if (batchRunning || pendingInvite)
            { Log("An add or summon operation is already in progress."); return; }
            if (tokens.Length == 0) { Log("Usage: /raidadd <name> [name...] or /raidadd <name> <position>"); return; }
            int position;
            if (tokens.Length == 2 && int.TryParse(tokens[1], out position))
            {
                AddAtPosition(tokens[0], position);
                return;
            }
            foreach (string token in tokens)
                if (int.TryParse(token, out position))
                { Log("Position can only follow one name: /raidadd <name> <position>."); return; }
            StartCoroutine(AddBatch(tokens));
        }

        private IEnumerator AddBatch(string[] names)
        {
            batchRunning = true;
            try
            {
                foreach (string name in names)
                {
                    RunCommand(true, name);
                    // The existing summon path is asynchronous; finish each summon before the next.
                    while (pendingInvite) yield return null;
                    yield return null;
                }
            }
            finally { batchRunning = false; }
        }

        private void AddAtPosition(string name, int position)
        {
            RaidMemberSlot slot = SlotAt(position);
            if (slot == null)
            { Log("Invalid position " + position + "; valid positions are 2-" +
                (GameData.RaidManager == null || GameData.RaidManager.Raiders == null ? "?" :
                 (GameData.RaidManager.Raiders.Count + 1).ToString()) + "."); return; }
            if (!GameData.RaidActive && !StartRaid()) return;
            if (!IsEmpty(slot))
            { Log("Position " + position + " is occupied by " +
                (slot.AssignedSimTracking == null ? "another member" : slot.AssignedSimTracking.SimName) + "."); return; }
            if (FindRaidSlot(GameData.RaidManager, name) != null)
            { Log(Canonical(name) + " is already in the raid."); return; }
            SimPlayer sim = FindActiveSim(name);
            if (sim == null)
            {
                SimPlayerTracking tracking = null;
                foreach (SimPlayerTracking record in GameData.SimMngr.Sims)
                    if (record != null && string.Equals(record.SimName, name, StringComparison.OrdinalIgnoreCase))
                    { tracking = record; break; }
                if (tracking == null) { Log("No SimPlayer named '" + name + "' was found."); return; }
                if (pendingInvite) { Log("A summon is already pending."); return; }
                if (SceneManager.GetActiveScene().name != "Reliquary")
                { Log(tracking.SimName + " is not active; summon requires Reliquary Hall."); return; }
                if (GameData.GuildManager == null || GameData.PlayerControl == null || GameData.ZoneAnnounce == null)
                { Log("Guild manager is not ready."); return; }
                var guild = GameData.GuildManager.GetGuildDataByID(GameData.PlayerControl.MyGuild);
                bool member = false;
                if (guild != null) foreach (string guildName in guild.GuildMembers)
                    if (string.Equals(guildName, tracking.SimName, StringComparison.OrdinalIgnoreCase))
                    { member = true; break; }
                if (!member || tracking.seekingPlayer || tracking.PersonalInvite || tracking.GoingToReliq ||
                    tracking.CurScene == SceneManager.GetActiveScene().name)
                { Log(tracking.SimName + " cannot currently be summoned."); return; }
                tracking.GoingToReliq = true;
                pendingInvite = true;
                StartCoroutine(SummonToPosition(tracking, position));
                Log("Summoning " + tracking.SimName + " for position " + position + ".");
                return;
            }
            if (sim.MySimTracking == null || sim.GetThisNPC() == null)
            { Log(Canonical(name) + " is not ready to join."); return; }
            GameData.RaidManager.AddToRosterAtSlot(sim, slot);
            Log(slot.AssignedSimTracking == sim.MySimTracking
                ? "Added " + sim.MySimTracking.SimName + " to position " + position + "."
                : "Could not confirm addition to position " + position + ".",
                slot.AssignedSimTracking == sim.MySimTracking ? "green" : "yellow");
        }

        private IEnumerator SummonToPosition(SimPlayerTracking tracking, int position)
        {
            try
            {
                yield return new WaitForSeconds(5f);
                if (!GameData.RaidActive || SceneManager.GetActiveScene().name != "Reliquary")
                { Log("Summon canceled: raid ended or zone changed."); yield break; }
                GameData.SimMngr.SpawnMeInPlayerZone(tracking,
                    SceneManager.GetActiveScene().name, GameData.ZoneAnnounce.transform.position, false);
                float deadline = Time.realtimeSinceStartup + 10f;
                while (FindActiveSim(tracking.SimName) == null && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (GameData.RaidActive) AddAtPosition(tracking.SimName, position);
            }
            finally { pendingInvite = false; }
        }

        private static RaidMemberSlot FindRaidSlot(RaidManager raid, string name)
        {
            foreach (RaidMemberSlot slot in raid.Raiders)
            {
                if (slot != null && slot.AssignedSimTracking != null &&
                    string.Equals(slot.AssignedSimTracking.SimName, name,
                        StringComparison.OrdinalIgnoreCase))
                    return slot;
            }
            return null;
        }

        private static SimPlayer FindActiveSim(string name)
        {
            foreach (SimPlayer sim in GameData.SimMngr.ActiveSimInstances)
            {
                if (sim != null && sim.MySimTracking != null &&
                    string.Equals(sim.MySimTracking.SimName, name,
                        StringComparison.OrdinalIgnoreCase))
                    return sim;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(TypeText), "CheckCommands")]
    internal static class SlashCommandPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TypeText __instance)
        {
            if (__instance == null || __instance.typed == null) return true;
            string text = __instance.typed.text.Trim();
            int space = text.IndexOf(' ');
            string command = space < 0 ? text : text.Substring(0, space);
            bool add = command.Equals("/raidadd", StringComparison.OrdinalIgnoreCase);
            bool remove = command.Equals("/raidremove", StringComparison.OrdinalIgnoreCase);
            bool replace = command.Equals("/raidreplace", StringComparison.OrdinalIgnoreCase);
            bool status = command.Equals("/raidstatus", StringComparison.OrdinalIgnoreCase);
            bool list = command.Equals("/raidlist", StringComparison.OrdinalIgnoreCase);
            bool start = command.Equals("/raidstart", StringComparison.OrdinalIgnoreCase);
            bool end = command.Equals("/raidend", StringComparison.OrdinalIgnoreCase);
            if (!add && !remove && !replace && !status && !list && !start && !end) return true;

            string argument = space < 0 ? "" : text.Substring(space + 1).Trim();
            if (Plugin.Instance != null)
            {
                if (start) Plugin.Instance.StartRaid();
                else if (end) Plugin.Instance.EndRaid();
                else if (status || list) Plugin.Instance.ShowStatus(list);
                else if (replace)
                {
                    // SimPlayer names contain no spaces. Accept either whitespace
                    // or a comma as the separator for backward compatibility.
                    string[] names = argument.Split(
                        new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (names.Length != 2)
                        Plugin.Log("Usage: /raidreplace <existing name> <new name>");
                    else
                        Plugin.Instance.Replace(names[0], names[1]);
                }
                else if (add)
                {
                    string[] tokens = argument.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    Plugin.Instance.RunAddTokens(tokens);
                }
                else
                {
                    string[] tokens = argument.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0) Plugin.Log("Usage: /raidremove <name...|position|start-end>");
                    else Plugin.Instance.RunRemoveTokens(tokens);
                }
            }
            return false;
        }
    }
}

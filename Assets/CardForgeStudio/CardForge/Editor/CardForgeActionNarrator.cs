using System;
using System.Linq;

namespace CardForge.Editor
{
    /// <summary>
    /// Generates readable descriptions from the same fields exported to the
    /// authoritative server. The graph is a visualization, not a second rule engine.
    /// </summary>
    public static class CardForgeActionNarrator
    {
        public static string Describe(ActionDocument action, ResolutionGraphNode step, int index)
        {
            if (action == null || step == null) return "No action or rule step selected.";
            var title = (step.title ?? "").Trim().ToLowerInvariant();

            if (title.Contains("viability") || title.Contains("gate") || index == 0 && title.Contains("trigger"))
                return (action.requiresActivePlayer
                    ? "Only the active player may start this action during an allowed phase."
                    : "Either player may propose this action when the rules grant priority.") +
                    (string.IsNullOrWhiteSpace(action.triggerId)
                        ? " No extra activation trigger is required."
                        : " It also requires the '" + Friendly(action.triggerId) + "' trigger.");

            if (title.Contains("declare") || title == "action")
                return "The player declares '" + Friendly(action.name) + "'." +
                    (string.IsNullOrWhiteSpace(action.sourceZoneId)
                        ? " The server checks that this action is currently available."
                        : " Choose a legal card from the " + Friendly(action.sourceZoneId) + " zone.");

            if (title.Contains("condition") || title.Contains("check"))
                return string.IsNullOrWhiteSpace(action.conditionKind)
                    ? "The server checks phase timing, player priority and any targeting restrictions."
                    : "The server evaluates '" + Friendly(action.conditionKind) + "'" +
                      (string.IsNullOrWhiteSpace(action.conditionKey) ? "" : " on " + Friendly(action.conditionKey)) +
                      (string.IsNullOrWhiteSpace(action.conditionValue) ? "" : " against " + action.conditionValue) +
                      ". If it fails, this action cannot resolve.";

            if (title.Contains("cost") || title.Contains("pay"))
                return action.costAmount > 0 && !string.IsNullOrWhiteSpace(action.costResourceId)
                    ? "Pay " + action.costAmount + " " + Friendly(action.costResourceId) +
                      " before resolving. If that resource is insufficient, the action is rejected."
                    : "There is no fixed server resource payment on this action." +
                      (!string.IsNullOrWhiteSpace(action.cost) ? " Printed cost reminder: " + action.cost + "." : "");

            if (title.Contains("target") || title.Contains("choose"))
                return !string.IsNullOrWhiteSpace(action.effectTarget)
                    ? "Choose " + Friendly(action.effectTarget) + " as the effect target. " +
                      "The server verifies it is legal before changing game state."
                    : !string.IsNullOrWhiteSpace(action.targets)
                    ? "Choose a legal target: " + action.targets + "."
                    : "No explicit target is needed unless the effect or source zone requires a card.";

            if (title.Contains("orient") || title.Contains("rotate") || title.Contains("flip"))
                return action.orientationChange == CardOrientationChange.None
                    ? "Leave the card orientation unchanged."
                    : Friendly(action.orientationChange.ToString()) + " for " +
                      Friendly(action.orientationTarget) + " once the action is legal.";

            if (title.Contains("effect") || title.Contains("apply") || title.Contains("resolve"))
                return DescribeEffect(action);

            if (title.Contains("emit") || title.Contains("event") || title.Contains("output"))
                return action.causeTriggerIds == null || action.causeTriggerIds.Count == 0
                    ? "No additional explicit trigger outputs are attached. Natural game events may still fire."
                    : "After the effect, emit " + string.Join(", ", action.causeTriggerIds.Select(Friendly)) +
                      " and give eligible players their response window.";

            if (title.Contains("complete") || title.Contains("finish"))
                return "Finish resolving '" + Friendly(action.name) +
                    "', update the game view and evaluate victory or defeat.";

            // Keep designer-authored custom steps rather than inventing behavior.
            return string.IsNullOrWhiteSpace(step.detail)
                ? "Custom step '" + Friendly(step.title) +
                  "'. Add its specific rule or link it to an executable action field."
                : step.detail;
        }

        public static string DescribeEffect(ActionDocument action)
        {
            var amount = Math.Abs(action.effectAmount);
            var count = amount == 0 ? 1 : amount;
            switch ((action.serverEffectKind ?? "").Trim().ToLowerInvariant())
            {
                case "move_card":
                    return "Move the chosen card from " + Zone(action.sourceZoneId, "its source zone") +
                           " to " + Zone(action.destinationZoneId, "the destination zone") +
                           ". The server updates visibility and ownership.";
                case "draw":
                    return "Draw " + count + " card" + (count == 1 ? "" : "s") + " from the deck into the hand.";
                case "modify_resource":
                    return (action.effectAmount < 0 ? "Reduce " : "Increase ") +
                           Friendly(action.resourceId) + " by " + amount +
                           TargetSuffix(action.effectTarget) + ".";
                case "drain_resource":
                    return "Transfer up to " + count + " " + Friendly(action.resourceId) +
                           " from the targeted opponent to the acting player.";
                case "scale_resource":
                    return "Change " + Friendly(action.resourceId) + " by " +
                           action.effectAmount + " per qualifying creature" +
                           TargetSuffix(action.effectTarget) + ".";
                case "set_data":
                    return "Write the key '" + action.dataKey + "' with the value '" +
                           action.dataValue + "' on the game object.";
                case "emit_event":
                    return "Emit the event '" + Friendly(action.eventType) +
                           "' so matching triggers and abilities may respond.";
                case "advance_phase":
                    return "Advance to the next phase according to the active turn flow.";
                case "end_turn":
                    return "End the current turn, clear temporary effects and hand priority to the next player.";
                case "finish_game":
                    return "Finish the match and resolve its winner using the configured victory rules.";
                case "level_mana_source":
                    return "Level up the active Mana Source if the required cost is paid; apply its new passive benefits.";
                case "attack_player":
                    return "Declare the chosen creature as an attacker against an opposing player. Resolve damage and combat reactions.";
                case "attack_creature":
                    return "Declare combat between the chosen attacker and enemy creature. Apply damage and defeat rules.";
                case "mill":
                    return "Move " + count + " card" + (count == 1 ? "" : "s") +
                           " from the targeted deck to its discard zone.";
                case "reclaim":
                    return "Return " + count + " card" + (count == 1 ? "" : "s") +
                           " from discard to the permitted destination.";
                case "destroy_target":
                    return "Defeat the selected opposing creature and move it to its appropriate destination.";
                default:
                    return "Apply the server action '" + Friendly(action.serverEffectKind) +
                           "'. Configure a supported effect kind to make this behavior executable.";
            }
        }

        private static string Friendly(string value) =>
            string.IsNullOrWhiteSpace(value) ? "unspecified" : value.Replace('_', ' ').Replace('-', ' ').Trim();

        private static string Zone(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : "the " + Friendly(value) + " zone";

        private static string TargetSuffix(string target) =>
            string.IsNullOrWhiteSpace(target) ? "" : " for " + Friendly(target);
    }
}

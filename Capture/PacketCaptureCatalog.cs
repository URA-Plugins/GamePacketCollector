using Gallop.Endpoints;
using UmamusumeResponseAnalyzer.Plugin;

namespace GamePacketCollector.Capture;

public static class PacketCaptureCatalog
{
    const string GameEndpointPathPrefix = "/umamusume";

    static readonly string[] SingleModeFixedEndpointPaths =
    [
        "/generate_succession/exec",
        "/generate_succession/factor_select",
        "/generate_succession/finish",
        "/generate_succession/index",
        "/generate_succession/load",
        "/generate_succession/priority_factor",
        "/idle_single_mode/check_progress_log",
        "/idle_single_mode/end",
        "/idle_single_mode/pre_start",
        "/idle_single_mode/result",
        "/idle_single_mode/start",
        "/idle_single_mode/status",
        "/pre_single_mode/friend_support_card_reload",
        "/pre_single_mode/get_succession_trained_chara",
        "/pre_single_mode/index",
    ];

    static readonly string[] SingleModeScenarioNames =
    [
        "",
        "arc",
        "breeders",
        "cook",
        "free",
        "legend",
        "live",
        "mecha",
        "onsen",
        "pioneer",
        "ramen",
        "sport",
        "team",
        "venus",
    ];

    static readonly string[] SingleModeSharedActionNames =
    [
        "check_event",
        "continue",
        "exec_command",
        "factor_lottery",
        "factor_order_load",
        "factor_reload",
        "factor_relottery_end",
        "factor_select",
        "finish",
        "finish_claw_crane",
        "gain_skills",
        "get_choice_reward",
        "load",
        "multi_race_reserve",
        "race_analyze",
        "race_end",
        "race_entry",
        "race_out",
        "race_start",
        "select_succession",
        "start",
    ];

    static readonly string[] SingleModeScenarioSpecificEndpointPaths =
    [
        "/single_mode_arc/arc_race_analyze",
        "/single_mode_arc/arc_race_continue",
        "/single_mode_arc/arc_race_end",
        "/single_mode_arc/arc_race_entry",
        "/single_mode_arc/arc_race_out",
        "/single_mode_arc/arc_race_start",
        "/single_mode_arc/potential_level_up",
        "/single_mode_arc/selection_exec",
        "/single_mode_breeders/bc_race_entry",
        "/single_mode_breeders/finish_team_union_event",
        "/single_mode_breeders/member_bc_race",
        "/single_mode_breeders/select_bc_race",
        "/single_mode_breeders/team_meeting",
        "/single_mode_breeders/team_review",
        "/single_mode_breeders/team_sp_training",
        "/single_mode_cook/cooking",
        "/single_mode_cook/facility_level_up",
        "/single_mode_cook/tasting_live",
        "/single_mode_free/multi_item_exchange",
        "/single_mode_free/multi_item_use",
        "/single_mode_legend/cm_end",
        "/single_mode_legend/exchange_buff",
        "/single_mode_legend/legend_race_continue",
        "/single_mode_legend/legend_race_end",
        "/single_mode_legend/legend_race_entry",
        "/single_mode_legend/legend_race_out",
        "/single_mode_legend/legend_race_start",
        "/single_mode_legend/popularity_end",
        "/single_mode_legend/select_buff",
        "/single_mode_live/live_start",
        "/single_mode_live/lottery_square",
        "/single_mode_live/master_square",
        "/single_mode_live/reserve_square",
        "/single_mode_mecha/mecha_live",
        "/single_mode_mecha/overdrive",
        "/single_mode_mecha/tuning",
        "/single_mode_mecha/upgrade_race",
        "/single_mode_onsen/assistant_exec",
        "/single_mode_onsen/bathing",
        "/single_mode_onsen/check_dug_result",
        "/single_mode_onsen/onsen_live",
        "/single_mode_onsen/select_dig_onsen",
        "/single_mode_pioneer/pioneer_complete",
        "/single_mode_pioneer/pioneer_live",
        "/single_mode_pioneer/planning",
        "/single_mode_pioneer/shima_training_exec",
        "/single_mode_ramen/ramen_live",
        "/single_mode_ramen/region_select_check",
        "/single_mode_ramen/save_skill_filter_set",
        "/single_mode_ramen/select_region",
        "/single_mode_ramen/uraf_effect_apply",
        "/single_mode_ramen/uraf_effect_select_event_checked",
        "/single_mode_sport/competition",
        "/single_mode_sport/competition_live",
        "/single_mode_sport/use_item",
        "/single_mode_team/opponent_list",
        "/single_mode_team/team_edit",
        "/single_mode_team/team_race_analyze",
        "/single_mode_team/team_race_continue",
        "/single_mode_team/team_race_end",
        "/single_mode_team/team_race_end_out",
        "/single_mode_team/team_race_out",
        "/single_mode_team/team_race_start",
        "/single_mode_venus/spirit_history",
        "/single_mode_venus/spirit_use",
        "/single_mode_venus/venus_race_continue",
        "/single_mode_venus/venus_race_end",
        "/single_mode_venus/venus_race_entry",
        "/single_mode_venus/venus_race_out",
        "/single_mode_venus/venus_race_start",
    ];

    static readonly string[] SingleModeEndpointPaths =
    [
        ..SingleModeFixedEndpointPaths,
        ..BuildSingleModeEndpointPaths(SingleModeScenarioNames, SingleModeSharedActionNames),
        ..BuildSingleModeEndpointPaths(["pioneer", "ramen"], ["check_point"]),
        ..BuildSingleModeEndpointPaths(["cook", "ramen"], ["tasting"]),
        ..SingleModeScenarioSpecificEndpointPaths,
    ];

    static readonly string[] RoomMatchEndpointPaths =
    [
        "/room_match/create_room",
        "/room_match/create_room_simple",
        "/room_match/destroy_room",
        "/room_match/edit_room",
        "/room_match/enter_waiting_room",
        "/room_match/entry_room",
        "/room_match/force_race_start",
        "/room_match/get_entry_room_list",
        "/room_match/get_saved_race_result",
        "/room_match/get_saved_race_result_list",
        "/room_match/index",
        "/room_match/polling",
        "/room_match/race_end_result",
        "/room_match/race_start",
        "/room_match/room_detail",
        "/room_match/room_list",
        "/room_match/room_search",
        "/room_match/watch_room",
    ];

    static readonly string[] RaceEndpointPaths =
    [
        "/challenge_match/challenge_race_open",
        "/challenge_match/index",
        "/challenge_match/race_end",
        "/challenge_match/race_entry",
        "/challenge_match/race_start",
        "/challenge_match/reflect_item_effect",
        "/challenge_match/resume",
        "/champions/all_race_exec",
        "/champions/entry",
        "/champions/final_lobby",
        "/champions/final_race_end",
        "/champions/final_race_ranking",
        "/champions/final_race_start",
        "/champions/get_race_history_info",
        "/champions/get_race_result_chart",
        "/champions/index",
        "/champions/lobby",
        "/champions/race_end",
        "/champions/race_entry",
        "/champions/race_start",
        "/champions/select_league",
        "/champions/set_entry_chara",
        "/daily_legend_race/index",
        "/daily_legend_race/race_entry",
        "/daily_legend_race/race_start",
        "/daily_legend_race/reflect_item_effect",
        "/daily_legend_race/replay_check",
        "/daily_legend_race/resume",
        "/daily_race/index",
        "/daily_race/race_entry",
        "/daily_race/race_start",
        "/daily_race/reflect_item_effect",
        "/daily_race/replay_check",
        "/daily_race/resume",
        "/legend_race/index",
        "/legend_race/race_entry",
        "/legend_race/race_start",
        "/legend_race/reflect_item_effect",
        "/legend_race/replay_check",
        "/legend_race/resume",
        "/main_story_race/get_race_table",
        "/main_story_race/race_end",
        "/main_story_race/race_start",
        "/practice_race/delete_race",
        "/practice_race/get_recommend_race_condition_list",
        "/practice_race/index",
        "/practice_race/race_end",
        "/practice_race/race_replay",
        "/practice_race/race_start",
        "/team_building/index",
        "/team_building/race_continue",
        "/team_building/race_entry",
        "/team_building/race_result",
        "/team_building/race_start",
        "/team_building/resume",
        "/team_stadium/all_race_end",
        "/team_stadium/index",
        "/team_stadium/opponent_list",
        "/team_stadium/ranking",
        "/team_stadium/replay_check",
        "/team_stadium/start",
        "/team_stadium/user_detail",
        "/team_stadium_skip/race_skip",
        "/ultimate_race/index",
        "/ultimate_race/race_entry",
        "/ultimate_race/race_start",
        "/ultimate_race/reflect_item_effect",
        "/ultimate_race/replay_check",
        "/ultimate_race/resume",
    ];

    static readonly string[] GachaEndpointPaths =
    [
        "/gacha/exec",
        "/gacha/get_history",
        "/gacha/get_prize_history",
    ];

    public static IReadOnlyList<PacketCaptureEndpoint> SelectedEndpoints { get; } =
    [
        ..Resolve("single-mode", SingleModeEndpointPaths),
        ..Resolve("room-match", RoomMatchEndpointPaths),
        ..Resolve("race", RaceEndpointPaths),
        ..Resolve("gacha", GachaEndpointPaths),
    ];

    public static IReadOnlyList<EndpointPattern> BuildPatterns(
        IReadOnlyList<PacketCaptureEndpoint> endpoints)
    {
        var remaining = endpoints.Select(endpoint => endpoint.Path)
            .ToHashSet(StringComparer.Ordinal);
        List<EndpointPattern> patterns = [];
        foreach (var action in SingleModeSharedActionNames)
        {
            var matching = GameEndpointCatalog.ByPath.Keys
                .Where(path => IsSingleModeAction(path, action))
                .ToArray();
            if (matching.Length == 0 || matching.Any(path => !remaining.Contains(path)))
                continue;

            patterns.Add(EndpointPattern.Wildcard($"/umamusume/single_mode*/{action}"));
            remaining.ExceptWith(matching);
        }

        patterns.AddRange(remaining.Order(StringComparer.Ordinal).Select(EndpointPattern.Exact));
        return patterns;
    }

    static bool IsSingleModeAction(string path, string action)
    {
        var segments = path.Split('/');
        return segments is ["", "umamusume", var scenario, var endpointAction] &&
               scenario.StartsWith("single_mode", StringComparison.Ordinal) &&
               string.Equals(endpointAction, action, StringComparison.Ordinal);
    }

    static IEnumerable<string> BuildSingleModeEndpointPaths(IEnumerable<string> scenarios, IEnumerable<string> actions)
    {
        foreach (var scenario in scenarios)
        {
            foreach (var action in actions)
                yield return BuildSingleModeEndpointPath(scenario, action);
        }
    }

    static string BuildSingleModeEndpointPath(string scenario, string action)
        => scenario.Length == 0
            ? $"/single_mode/{action}"
            : $"/single_mode_{scenario}/{action}";

    static IEnumerable<PacketCaptureEndpoint> Resolve(string group, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var catalogPath = GameEndpointPathPrefix + path;
            if (!GameEndpointCatalog.ByPath.TryGetValue(catalogPath, out var endpoint))
                throw new InvalidOperationException($"Gallop endpoint not found: {catalogPath}");

            yield return new(endpoint.EndpointType, endpoint.Path, group);
        }
    }
}

public sealed record PacketCaptureEndpoint(
    Type EndpointType,
    string Path,
    string Group);

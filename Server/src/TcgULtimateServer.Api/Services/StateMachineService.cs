using System.Text.Json;
using System.Text.Json.Nodes;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed class StateMachineService(AppState state)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public StateMachineConfiguration Upload(Stream stream)
    {
        StateMachineConfiguration configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<StateMachineConfiguration>(stream, JsonOptions)
                ?? throw new InvalidOperationException("Invalid state machine configuration JSON.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Invalid state machine configuration JSON.", exception);
        }

        return Save(configuration);
    }

    public StateMachineConfiguration Save(StateMachineConfiguration configuration)
    {
        var normalized = Normalize(configuration);
        ValidateOrThrow(normalized);
        state.StateMachineConfigurations[normalized.Id] = normalized;
        return normalized;
    }

    public StateMachineValidationResult ValidateConfiguration(StateMachineConfiguration configuration)
    {
        try
        {
            var normalized = Normalize(configuration);
            ValidateOrThrow(normalized);
            return new StateMachineValidationResult(true, [], normalized);
        }
        catch (InvalidOperationException exception)
        {
            return new StateMachineValidationResult(false, [exception.Message]);
        }
    }

    public StateMachineConfiguration GetConfiguration(string id) =>
        state.StateMachineConfigurations.TryGetValue(id, out var configuration)
            ? configuration
            : throw new KeyNotFoundException("State machine configuration not found.");

    public StateMachineInstance Create(CreateStateMachineRequest request, string ownerId)
    {
        var configuration = GetConfiguration(request.ConfigurationId);
        var participants = (request.ParticipantIds ?? [])
            .Append(ownerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var instance = new StateMachineInstance
        {
            ConfigurationId = configuration.Id,
            OwnerId = ownerId,
            ParticipantIds = participants,
            CurrentState = configuration.InitialState,
            Data = request.Data?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()) ?? [],
            Context = request.Data?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()) ?? []
        };
        instance.Events.Add(new StateMachineEvent("started", "", instance.CurrentState, Data: null, At: DateTimeOffset.UtcNow));
        state.StateMachineInstances[instance.Id] = instance;
        return instance;
    }

    public bool CanAccess(StateMachineInstance instance, string userId, bool isAdministrator) =>
        isAdministrator || string.Equals(instance.OwnerId, userId, StringComparison.Ordinal) ||
        instance.ParticipantIds.Contains(userId, StringComparer.Ordinal);

    public StateMachineInstance GetInstance(string id) =>
        state.StateMachineInstances.TryGetValue(id, out var instance)
            ? instance
            : throw new KeyNotFoundException("State machine instance not found.");

    public StateMachineInstance Transition(string id, string actorId, TransitionStateMachineRequest request)
    {
        var instance = GetInstance(id);
        if (instance.Finished)
            throw new InvalidOperationException("The state machine has finished.");

        var configuration = GetConfiguration(instance.ConfigurationId);
        var transition = configuration.Transitions.FirstOrDefault(candidate =>
            string.Equals(candidate.Event, request.Event, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.From, instance.CurrentState, StringComparison.OrdinalIgnoreCase) &&
            GuardsMatch(candidate.Guards, instance.Context, request.Data));

        if (transition is null)
            throw new InvalidOperationException($"Event '{request.Event}' is not valid from state '{instance.CurrentState}'.");

        var from = instance.CurrentState;
        instance.CurrentState = transition.To;
        ApplyEffects(instance.Context, transition.Effects, request.Data);
        instance.Finished = configuration.TerminalStates?.Contains(instance.CurrentState, StringComparer.OrdinalIgnoreCase) == true;
        instance.Events.Add(new StateMachineEvent(
            request.Event,
            from,
            transition.To,
            actorId,
            request.Data?.Clone() ?? transition.Data?.Clone(),
            DateTimeOffset.UtcNow));
        return instance;
    }

    private static void ValidateOrThrow(StateMachineConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Id) ||
            string.IsNullOrWhiteSpace(configuration.Name) ||
            string.IsNullOrWhiteSpace(configuration.Version) ||
            string.IsNullOrWhiteSpace(configuration.InitialState) ||
            configuration.States is null ||
            configuration.States.Count == 0 ||
            configuration.Transitions is null)
            throw new InvalidOperationException("A state machine needs an id, name, version, initialState, and states.");

        var states = configuration.States.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!states.Contains(configuration.InitialState))
            throw new InvalidOperationException("The initialState must be one of the configured states.");

        if (configuration.Transitions.Any(transition =>
            string.IsNullOrWhiteSpace(transition.Event) ||
            !states.Contains(transition.From) ||
            !states.Contains(transition.To)))
            throw new InvalidOperationException("Every transition needs an event and valid from/to states.");

        if (configuration.TerminalStates?.Any(terminal => !states.Contains(terminal)) == true)
            throw new InvalidOperationException("Every terminal state must be one of the configured states.");

    }

    private static StateMachineConfiguration Normalize(StateMachineConfiguration configuration) => configuration with
    {
        States = configuration.States ?? [],
        Transitions = configuration.Transitions ?? [],
        TerminalStates = configuration.TerminalStates ?? [],
        Metadata = configuration.Metadata ?? new Dictionary<string, JsonElement>(),
        StateMetadata = configuration.StateMetadata ?? new Dictionary<string, Dictionary<string, JsonElement>>()
    };

    private static bool GuardsMatch(Dictionary<string, JsonElement>? guards, Dictionary<string, JsonElement> context, JsonElement? eventData)
    {
        if (guards is null || guards.Count == 0) return true;
        return guards.All(guard => EvaluateGuard(ReadPath(context, guard.Key, eventData), guard.Value));
    }

    private static void ApplyEffects(Dictionary<string, JsonElement> context, Dictionary<string, JsonElement>? effects, JsonElement? eventData)
    {
        if (effects is null) return;
        var root = JsonNode.Parse(JsonSerializer.Serialize(context))?.AsObject() ?? [];
        foreach (var effect in effects)
        {
            var operation = effect.Value.ValueKind == JsonValueKind.Object
                ? effect.Value.EnumerateObject().FirstOrDefault(property => property.Name.StartsWith('$')).Name
                : "";
            var value = operation.Length == 0 ? effect.Value : effect.Value.GetProperty(operation);
            switch (operation)
            {
                case "$increment":
                    SetPath(root, effect.Key, GetNumber(ReadPath(root, effect.Key)) + GetNumber(value));
                    break;
                case "$append":
                    var array = ReadPath(root, effect.Key)?.AsArray() ?? new JsonArray();
                    array.Add(JsonNode.Parse(value.GetRawText()));
                    SetPath(root, effect.Key, array);
                    break;
                case "$fromEvent":
                    if (eventData.HasValue && value.ValueKind == JsonValueKind.String)
                        SetPath(root, effect.Key, ReadPath(eventData.Value, value.GetString() ?? ""));
                    break;
                case "$remove":
                    RemovePath(root, effect.Key);
                    break;
                default:
                    SetPath(root, effect.Key, JsonNode.Parse(value.GetRawText()));
                    break;
            }
        }

        context.Clear();
        foreach (var property in root)
            context[property.Key] = JsonSerializer.SerializeToElement(property.Value);
    }

    private static bool EvaluateGuard(JsonElement? actual, JsonElement expected)
    {
        if (expected.ValueKind != JsonValueKind.Object || !expected.EnumerateObject().Any(property => property.Name.StartsWith('$')))
            return actual.HasValue && JsonEquals(actual.Value, expected);

        foreach (var operation in expected.EnumerateObject())
        {
            var matches = operation.Name switch
            {
                "$exists" => actual.HasValue == (operation.Value.ValueKind == JsonValueKind.True),
                "$equals" => actual.HasValue && JsonEquals(actual.Value, operation.Value),
                "$in" => actual.HasValue && operation.Value.ValueKind == JsonValueKind.Array && operation.Value.EnumerateArray().Any(item => JsonEquals(actual.Value, item)),
                "$gt" => actual.HasValue && GetNumber(actual.Value) > GetNumber(operation.Value),
                "$gte" => actual.HasValue && GetNumber(actual.Value) >= GetNumber(operation.Value),
                "$lt" => actual.HasValue && GetNumber(actual.Value) < GetNumber(operation.Value),
                "$lte" => actual.HasValue && GetNumber(actual.Value) <= GetNumber(operation.Value),
                _ => throw new InvalidOperationException($"Unsupported guard operator '{operation.Name}'.")
            };
            if (!matches) return false;
        }
        return true;
    }

    private static JsonElement? ReadPath(Dictionary<string, JsonElement> context, string path, JsonElement? eventData = null)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(context));
        if (eventData.HasValue && root is JsonObject objectRoot)
            objectRoot["$event"] = JsonNode.Parse(eventData.Value.GetRawText());
        var node = ReadPath(root, path);
        return node is null ? null : JsonSerializer.SerializeToElement(node);
    }

    private static JsonNode? ReadPath(JsonElement root, string path)
    {
        var node = JsonNode.Parse(root.GetRawText());
        return ReadPath(node, path);
    }

    private static JsonNode? ReadPath(JsonNode? root, string path)
    {
        JsonNode? current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current?[segment];
            if (current is null) return null;
        }
        return current;
    }

    private static void SetPath(JsonObject root, string path, JsonNode? value)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return;
        JsonObject current = root;
        foreach (var segment in segments[..^1])
        {
            if (current[segment] is not JsonObject child)
            {
                child = new JsonObject();
                current[segment] = child;
            }
            current = child;
        }
        current[segments[^1]] = value;
    }

    private static void RemovePath(JsonObject root, string path)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return;
        var parent = ReadPath(root, string.Join('.', segments[..^1])) as JsonObject;
        parent?.Remove(segments[^1]);
    }

    private static bool JsonEquals(JsonElement left, JsonElement right) =>
        string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);

    private static decimal GetNumber(JsonElement? value) =>
        value?.TryGetDecimal(out var number) == true ? number : 0;

    private static decimal GetNumber(JsonNode? value) =>
        value is JsonValue jsonValue && jsonValue.TryGetValue<decimal>(out var number) ? number : 0;
}

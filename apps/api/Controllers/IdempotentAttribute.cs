namespace Api.Controllers;

/// <summary>Marks an action that reads <c>Idempotency-Key</c> and may answer <c>Idempotent-Replayed</c>, so the OpenAPI document can say so.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute;

namespace Api.IntegrationTests;

/// <summary>A <see cref="FactAttribute"/> that skips, with the reason, when there is no integration database.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactRequiresDatabaseAttribute : FactAttribute
{
    public FactRequiresDatabaseAttribute()
    {
        if (IntegrationDatabase.ConnectionString is null)
        {
            Skip = IntegrationDatabase.SkipReason;
        }
    }
}

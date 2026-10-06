namespace TheIsleOverlay.Core;

/// <summary>Presentation of received delta state; does not change field freshness.</summary>
public static class InboundVitalsDisplay
{
    public static ExactVitals? Resolve(ExactVitals? current, ExactVitals? retained)
    {
        if (retained is null) return current;
        current ??= new ExactVitals();
        return current with
        {
            Growth = current.Growth ?? retained.Growth,
            Health = current.Health ?? retained.Health, MaxHealth = current.MaxHealth ?? retained.MaxHealth,
            Stamina = current.Stamina ?? retained.Stamina, MaxStamina = current.MaxStamina ?? retained.MaxStamina,
            Hunger = current.Hunger ?? retained.Hunger, MaxHunger = current.MaxHunger ?? retained.MaxHunger,
            Thirst = current.Thirst ?? retained.Thirst, MaxThirst = current.MaxThirst ?? retained.MaxThirst
        };
    }
}

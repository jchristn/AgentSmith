namespace Hugo.Core.Enums
{
    /// <summary>Access policy for a profile's standalone chat link.</summary>
    public enum ChatAccessModeEnum
    {
        /// <summary>Visitors authenticate and use their own permissions.</summary>
        Authenticated,
        /// <summary>Visitors use the explicitly selected publishing credential.</summary>
        Public
    }
}

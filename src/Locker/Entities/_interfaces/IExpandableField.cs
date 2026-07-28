namespace Locker
{
    /// <summary>Represents a non-generic expandable field.</summary>
    public interface IExpandableField
    {
        /// <summary>Gets or sets the ID.</summary>
        /// <value>The ID.</value>
        string? Id { get; set; }

        /// <summary>Gets or sets the expanded object.</summary>
        /// <value>The expanded object.</value>
        object? ExpandedObject { get; set; }

        /// <summary>
        /// Gets a value indicating whether the <see cref="IExpandableField"/> is expanded.
        /// </summary>
        /// <value>
        /// <c>true</c> if the <see cref="IExpandableField"/> is expanded; otherwise, <c>false</c>.
        /// </value>
        bool IsExpanded { get; }
    }

    public interface IExpandableField<T> : IExpandableField
        where T : IHasId
    {
        new T? ExpandedObject { get; set; }
    }
}

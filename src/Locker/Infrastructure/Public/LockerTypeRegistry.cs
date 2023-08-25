namespace Locker
{
    using System.Collections.ObjectModel;
    using System.Reflection;


    public static class LockerTypeRegistry
    {
        /// <summary>
        /// Dictionary mapping the values contained in the `object` key of JSON payloads returned
        /// by Locker to concrete types of model classes.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Type> ObjectToTypes = new ReadOnlyDictionary<string, Type>(
            new Dictionary<string, Type>
            {
                { "environment", typeof(Environment) },
                { "secret", typeof(Secret) },
            });

        /// <summary>
        /// Returns the concrete type to use, given a potential type and the value of the `object`
        /// key in a JSON payload.
        /// </summary>
        /// <param name="potentialType">Potential type. Can be a concrete type or an interface.</param>
        /// <param name="objectValue">Value of the `object` key in the JSON payload.</param>
        /// <returns>The concrete type to use, or `null`.</returns>
        public static Type GetConcreteType(Type potentialType, string objectValue)
        {
            if (potentialType != null && !potentialType.GetTypeInfo().IsInterface)
            {
                return potentialType;
            }

            Type concreteType = null;
            if (!string.IsNullOrEmpty(objectValue) && ObjectToTypes.TryGetValue(objectValue, out concreteType))
            {
                if (potentialType.GetTypeInfo().IsAssignableFrom(concreteType.GetTypeInfo()))
                {
                    return concreteType;
                }
            }

            return null;
        }
    }
}
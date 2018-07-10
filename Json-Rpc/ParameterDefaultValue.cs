namespace AustinHarris.JsonRpc
{
    /// <summary>
    ///     Holds default value for parameters.
    /// </summary>
    public class ParameterDefaultValue
    {
        public ParameterDefaultValue(string name, object value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>
        ///     Name of the parameter.
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     Default value for the parameter.
        /// </summary>
        public object Value { get; }
    }
}
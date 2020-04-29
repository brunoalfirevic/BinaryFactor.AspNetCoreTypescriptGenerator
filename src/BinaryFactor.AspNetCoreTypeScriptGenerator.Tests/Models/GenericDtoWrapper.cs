namespace BinaryFactor.AspNetCoreTypeScriptGenerator.Tests.Models
{
    public class GenericDtoWrapper<K, V>
    {
        public K Key{ get; set; }
        public V Value { get; set; }
    }

    public class NullableValueTypeWrapper<T>
        where T: struct
    {
        public T? Value { get; set; }
    }
}

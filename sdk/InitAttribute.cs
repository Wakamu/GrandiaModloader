namespace Grandia.Sdk;

/// <summary>
/// Called once after the <see cref="ModAttribute"/> type is constructed.
/// Signature is <c>void Init()</c> or <c>void Init(ModContext ctx)</c>.
/// Name is free. Register hook / service classes from here.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InitAttribute : Attribute
{
}

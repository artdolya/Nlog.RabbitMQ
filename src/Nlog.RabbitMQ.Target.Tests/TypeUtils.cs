using System.Reflection;

namespace NLog.Targets.RabbitMQ.Tests;

public static class TypeUtils
{
    public static void InvokeMethod<T>(this T target, string methodName, params object?[] args)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }
        if (string.IsNullOrEmpty(methodName))
        {
            throw new ArgumentNullException(nameof(methodName));
        }
        
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            throw new MissingMethodException($"Method {methodName} not found");
        } 
        
        method.Invoke(target, args);
    }
}
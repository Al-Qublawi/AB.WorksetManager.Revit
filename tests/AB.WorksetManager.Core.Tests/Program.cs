using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AB.WorksetManager.Core.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class TestAttribute : Attribute { }

    internal sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    internal static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertionException(what + ": expected " + expected + ", got " + actual);
        }

        public static void Near(double expected, double actual, double tolerance, string what)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new AssertionException(what + ": expected " + expected.ToString("0.###") + " ±" + tolerance + ", got " + actual.ToString("0.###"));
        }
    }

    /// <summary>
    /// Self-test for the Revit-free logic. No Revit, no test framework:
    ///     dotnet run --project tests\AB.WorksetManager.Core.Tests
    /// Exit code is the number of failures.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            var tests = typeof(Program).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .Where(m => filter == null || (m.DeclaringType.Name + "." + m.Name).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.MetadataToken)
                .ToList();

            int failed = 0;
            Console.WriteLine("AB Worksets Manager - core self-test (" + tests.Count + " tests)");
            foreach (MethodInfo test in tests)
            {
                string name = test.DeclaringType.Name + "." + test.Name;
                try
                {
                    test.Invoke(null, null);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  pass  " + name);
                }
                catch (TargetInvocationException ex)
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Exception inner = ex.InnerException ?? ex;
                    Console.WriteLine("  FAIL  " + name + " - " + inner.Message);
                    if (!(inner is AssertionException)) Console.WriteLine(inner.StackTrace);
                }
                finally
                {
                    Console.ResetColor();
                }
            }

            Console.WriteLine(failed == 0 ? "All " + tests.Count + " passed." : failed + " of " + tests.Count + " failed.");
            return failed;
        }
    }
}

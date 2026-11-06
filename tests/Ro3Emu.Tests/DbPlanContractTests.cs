using System.Reflection;
using Microsoft.Data.Sqlite;
using Ro3Emu.Server.Accounts.Persistence;

namespace Ro3Emu.Tests;

public sealed class DbPlanContractTests
{
    private static bool HasExecutor(Type type, Type contract)
    {
        const BindingFlags methods = BindingFlags.Instance | BindingFlags.NonPublic;
        var contextual = type.GetMethod("Execute", methods, null, [typeof(DbPlanExecution)], null);
        var native = type.GetMethod("Execute", methods, null, [typeof(SqliteConnection), typeof(SqliteTransaction)], null);
        return contextual is not null && contextual.DeclaringType != contract
            || native is not null && native.DeclaringType != contract;
    }

    [Fact]
    public void EveryConcreteEnginePlanImplementsAnExecutionRoute()
    {
        foreach (var contract in new[] { typeof(DbPlan), typeof(DbReadPlan) })
        {
            var missing = contract.Assembly.GetTypes().Where(type => !type.IsAbstract && contract.IsAssignableFrom(type)
                && !HasExecutor(type, contract)).Select(type => type.FullName).ToArray();
            Assert.Empty(missing);
        }
    }

    private sealed class Missing : DbPlan
    {
        internal override int Bytes => 1;
        internal override string Label => "test/missing-executor";
    }

    [Fact]
    public void APlanThatOnlyInheritsTheThrowingDefaultsFailsTheContractCheck() =>
        Assert.False(HasExecutor(typeof(Missing), typeof(DbPlan)));
}

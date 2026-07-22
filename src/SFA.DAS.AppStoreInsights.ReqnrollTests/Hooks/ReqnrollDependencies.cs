using Reqnroll;
using Reqnroll.BoDi;
using SFA.DAS.AppStoreInsights.ReqnrollTests.TestInfrastructure;

namespace SFA.DAS.AppStoreInsights.ReqnrollTests.Hooks;

[Binding]
public class ReqnrollDependencies
{
    [BeforeTestRun]
    public static void BeforeTestRun(ObjectContainer container)
    {
        container.RegisterTypeAs<TestRunContext, TestRunContext>();
    }
}
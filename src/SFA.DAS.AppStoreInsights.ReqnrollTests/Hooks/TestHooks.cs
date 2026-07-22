using Reqnroll;
using SFA.DAS.AppStoreInsights.ReqnrollTests.TestInfrastructure;

namespace SFA.DAS.AppStoreInsights.ReqnrollTests.Hooks;

[Binding]
public class TestHooks
{
    private readonly TestRunContext _testRunContext;

    public TestHooks(TestRunContext testRunContext)
    {
        _testRunContext = testRunContext;
    }

    [BeforeScenario]
    public void BeforeScenario()
    {
        _testRunContext.Reset();
    }
}
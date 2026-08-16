using AngryMonkey.CloudPayments;
using AngryMonkey.CloudPayments.MyFatoorah;

namespace Microsoft.Extensions.DependencyInjection;

public static class MyFatoorahCloudPaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddMyFatoorahCloudPayments(this IServiceCollection services, Action<MyFatoorahOptions> configure)
    {
        MyFatoorahOptions options = new();
        configure(options);
        services.AddSingleton(options);
        services.AddHttpClient<MyFatoorahPaymentProvider>(client => client.BaseAddress = options.ApiBaseAddress);
        services.AddSingleton<IPaymentProvider>(provider => provider.GetRequiredService<MyFatoorahPaymentProvider>());
        return services;
    }
}
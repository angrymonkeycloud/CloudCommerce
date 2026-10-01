using AngryMonkey.CloudMate;
using Microsoft.Extensions.Configuration;

// Packs and publishes every CloudCommerce package in one run. The version and the
// shared metadata below are read from CloudCommerce.Package.csproj and written into
// each target project, so all packages always ship as a matched set.
//
// Order matters: dependencies are packed before the projects that depend on them.
// The demo and the test projects are not packages and are deliberately absent.
// When you add a new packable library, add it to the Projects list below.

ConfigurationBuilder builder = new();

builder
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appconfig.json", optional: false, reloadOnChange: true)
    .AddUserSecrets<Program>();

IConfigurationRoot configuration = builder.Build();
string? apiKey = configuration["NuGetApiKey"];

await new CloudPack(new CloudPackConfig() { NugetApiKey = apiKey })
{
    MetadataProperies =
    [
        "PropertyGroup/Authors",
        "PropertyGroup/Company",
        "PropertyGroup/AssemblyVersion",
        "PropertyGroup/FileVersion",
        "PropertyGroup/PackageIcon"
    ],
    Projects =
    [
        // Payments
        new CloudPackProject("CloudPayments"),
        new CloudPackProject("CloudPayments.Stripe"),
        new CloudPackProject("CloudPayments.PayPal"),
        new CloudPackProject("CloudPayments.Adyen"),
        new CloudPackProject("CloudPayments.MyFatoorah"),
        new CloudPackProject("CloudPayments.SkipCash"),
        new CloudPackProject("CloudPayments.Tap"),
        new CloudPackProject("CloudPayments.PayTabs"),

        // Logistics
        new CloudPackProject("CloudLogistics"),
        new CloudPackProject("CloudLogistics.Aramex"),
        new CloudPackProject("CloudLogistics.DhlExpress"),
        new CloudPackProject("CloudLogistics.FedEx"),

        // Booking
        new CloudPackProject("CloudBooking"),

        // Commerce (depends on Payments, Logistics and Booking)
        new CloudPackProject("CloudCommerce"),
        new CloudPackProject("CloudCommerce.Components"),
    ]
}.Pack();

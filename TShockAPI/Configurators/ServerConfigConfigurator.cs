using System.IO;
using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TerrariaApi.Configurator;


namespace TShockAPI.Configurators;

/// <summary>
/// Configures the configuration provider, adding TShock's configuration options
/// </summary>
public class ServerConfigConfigurator : ConfigConfigurator
{
	/// <summary>
	/// The name of the file containing TShock's json configuration
	/// </summary>
	const string TSHOCK_CONFIG_FILE = "tshock.json";

	/// <summary>
	/// Construct a new ServerConfigConfigurator, setting a priority of 100
	/// </summary>
	public ServerConfigConfigurator() : base()
	{
		Priority = 100; // Arbitrarily higher than 0. We need this to run before the log configurator so that the logging configuration is present on the host context
	}

	/// <inheritdoc/>
	public override void Configure(HostBuilderContext hostContext, IConfigurationBuilder configBuilder, string[] args)
	{
		// Configuration is loaded as such:
		// commandline > environment vars > json configuration.
		// Environment variables and commandline are built first here, to determine if an alternative config path has been set.
		IConfiguration tshockEnvVars = new ConfigurationBuilder()
			.AddEnvironmentVariables("TSHOCK_")
			.AddCommandLine(args)
			.Build();

		// To set the config root via environment variable: export TSHOCK_CONFIG__ROOT=~/my/config dir/
		string configRoot = tshockEnvVars.GetSection("config:root").Value ?? AppContext.BaseDirectory;

		// We now build a second IConfiguration that first takes values from the json file, 
		// then overwrites any duplicates with values from the environment variables, then the commandline
		IConfiguration tshockConfig = new ConfigurationBuilder()
			.SetBasePath(configRoot)
			.AddJsonFile(TSHOCK_CONFIG_FILE, optional: true, reloadOnChange: true)
			.AddConfiguration(tshockEnvVars)
			.Build();

		configBuilder.AddConfiguration(tshockConfig);
	}
}

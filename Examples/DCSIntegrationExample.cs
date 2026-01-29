using PharmaceuticalProcess.DCS.Control;
using PharmaceuticalProcess.DCS.Core;
using PharmaceuticalProcess.HardwareComponents.Actuators;
using PharmaceuticalProcess.HardwareComponents.Core;
using PharmaceuticalProcess.HardwareComponents.ProcessEquipment;
using PharmaceuticalProcess.HardwareComponents.Sensors;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PharmaceuticalProcess.Examples
{
	/// <summary>
	/// Example demonstrating DCS integration with pharmaceutical process equipment
	/// </summary>
	public class DCSIntegrationExample
	{
		private DCSController _dcs;
		private ProcessUnit _fermentationUnit;
		private ProcessUnit _chromatographyUnit;
		private ProcessUnit _fillingUnit;

		public async Task RunExampleAsync()
		{
			Console.WriteLine("=== Pharmaceutical DCS Integration Example ===\n");

			// Step 1: Initialize DCS
			await InitializeDCSAsync();

			// Step 2: Configure Process Units
			ConfigureProcessUnits();

			// Step 3: Add Control Loops
			AddControlLoops();

			// Step 4: Create and Load Recipes
			LoadRecipes();

			// Step 5: Start DCS
			await _dcs.StartAsync();
			Console.WriteLine("DCS Started successfully\n");

			// Step 6: Run a batch
			await RunBatchProductionAsync();

			// Step 7: Monitor system
			await MonitorSystemAsync();

			// Step 8: Generate reports
			GenerateReports();

			// Step 9: Stop DCS
			await _dcs.StopAsync();
			Console.WriteLine("\nDCS Stopped");
		}

		private async Task InitializeDCSAsync()
		{
			Console.WriteLine("Initializing DCS...");
			_dcs = new DCSController(scanCycleMs: 100, enableRedundancy: false);
			await Task.CompletedTask;
		}

		private void ConfigureProcessUnits()
		{
			Console.WriteLine("Configuring Process Units...");

			// Create Fermentation Unit
			_fermentationUnit = new ProcessUnit("FERM-01", "Bioreactor 1000L", "Primary fermentation vessel");

			// Add fermentation equipment
			var bioreactor = new Bioreactor("BIO-001", "Bioreactor 1000L", 1000.0, BioreactorType.Stirred);
			var tempSensor = new TemperatureSensor("TE-001", "Reactor Temperature");
			var phSensor = new pHSensor("AE-001", "Reactor pH");

			_fermentationUnit.AddDevice(bioreactor);
			_fermentationUnit.AddDevice(tempSensor);
			_fermentationUnit.AddDevice(phSensor);

			// Create Chromatography Unit
			_chromatographyUnit = new ProcessUnit("CHROM-01", "Chromatography Skid", "Protein purification system");

			var chromSkid = new ChromatographySkid("CHROM-001", "AKTA Pure", ChromatographyType.AffinityCapture);
			var uvSensor = new UVSensor("UV-001", "Column Outlet UV", 280);

			_chromatographyUnit.AddDevice(chromSkid);
			_chromatographyUnit.AddDevice(uvSensor);

			// Create Filling Unit
			_fillingUnit = new ProcessUnit("FILL-01", "Aseptic Filling Line", "Vial filling and stoppering");

			var fillingMachine = new FillingMachine("FILL-001", "Vial Filler", maxFillingSpeed: 300, needleCount: 6);
			var tunnel = new SterilizingTunnel("TUNNEL-001", "Depyrogenation Tunnel",
				new TunnelConfiguration(TunnelConfigType.ProductionScale), vialThroughputCapacity: 300);

			_fillingUnit.AddDevice(fillingMachine);
			_fillingUnit.AddDevice(tunnel);

			// Add units to DCS
			_dcs.AddProcessUnit(_fermentationUnit);
			_dcs.AddProcessUnit(_chromatographyUnit);
			_dcs.AddProcessUnit(_fillingUnit);

			Console.WriteLine($"  - Added {_fermentationUnit.Name}");
			Console.WriteLine($"  - Added {_chromatographyUnit.Name}");
			Console.WriteLine($"  - Added {_fillingUnit.Name}\n");
		}

		private void AddControlLoops()
		{
			Console.WriteLine("Adding Control Loops...");

			// Temperature Control Loop for Fermentation
			var tempLoop = new PIDControlLoop("TC-001", "Bioreactor Temperature Control")
			{
				Kp = 2.0,
				Ki = 0.5,
				Kd = 0.1,
				Setpoint = 37.0,
				OutputMin = 0.0,
				OutputMax = 100.0,
				EnableAntiWindup = true,
				ReadProcessVariable = () => _fermentationUnit.GetProcessValue("FERM-01.TE-001.Temperature"),
				WriteOutput = (output) => { /* Write to heating/cooling system */ }
			};

			_fermentationUnit.AddControlLoop(tempLoop);
			_dcs.AddControlLoop(tempLoop);
			tempLoop.SetMode(ControlMode.Automatic);
			tempLoop.Start();

			Console.WriteLine($"  - Added {tempLoop.Description}");

			// pH Control Loop
			var phLoop = new PIDControlLoop("AC-001", "Bioreactor pH Control")
			{
				Kp = 1.5,
				Ki = 0.3,
				Kd = 0.05,
				Setpoint = 7.0,
				OutputMin = 0.0,
				OutputMax = 100.0,
				EnableAntiWindup = true,
				ReadProcessVariable = () => _fermentationUnit.GetProcessValue("FERM-01.AE-001.pH"),
				WriteOutput = (output) => { /* Write to acid/base dosing pumps */ }
			};

			_fermentationUnit.AddControlLoop(phLoop);
			_dcs.AddControlLoop(phLoop);
			phLoop.SetMode(ControlMode.Automatic);
			phLoop.Start();

			Console.WriteLine($"  - Added {phLoop.Description}\n");
		}

		private void LoadRecipes()
		{
			Console.WriteLine("Loading Production Recipes...");

			// Create Fermentation Recipe
			var fermRecipe = new Recipe
			{
				RecipeId = Guid.NewGuid().ToString(),
				Name = "Standard mAb Fermentation",
				Version = "1.0",
				Description = "14-day fed-batch fermentation for monoclonal antibody production"
			};

			// Add fermentation phases
			fermRecipe.Phases.Add(new BatchPhase
			{
				Name = "Inoculation",
				Description = "Transfer seed culture and start fermentation",
				Duration = 30, // 30 minutes
				Operations = new List<BatchOperation>
				{
					new BatchOperation
					{
						Name = "Transfer Inoculum",
						Description = "Transfer 100L seed culture to production bioreactor",
						Action = (parameters) =>
						{
							Console.WriteLine("  [Operation] Transferring inoculum...");
							// Transfer logic here
						}
					}
				}
			});

			fermRecipe.Phases.Add(new BatchPhase
			{
				Name = "Growth Phase",
				Description = "Cell growth and protein expression",
				Duration = 7 * 24 * 60, // 7 days in minutes
				Operations = new List<BatchOperation>
				{
					new BatchOperation
					{
						Name = "Monitor Growth",
						Description = "Monitor cell density and viability",
						Action = (parameters) =>
						{
							Console.WriteLine("  [Operation] Monitoring cell growth...");
						}
					}
				}
			});

			fermRecipe.Phases.Add(new BatchPhase
			{
				Name = "Production Phase",
				Description = "Sustained protein production",
				Duration = 7 * 24 * 60, // 7 days in minutes
				Operations = new List<BatchOperation>
				{
					new BatchOperation
					{
						Name = "Fed-Batch Feeding",
						Description = "Automated nutrient feeding",
						Action = (parameters) =>
						{
							Console.WriteLine("  [Operation] Feeding nutrients...");
						}
					}
				}
			});

			fermRecipe.Phases.Add(new BatchPhase
			{
				Name = "Harvest",
				Description = "Harvest cell culture and prepare for purification",
				Duration = 120, // 2 hours
				Operations = new List<BatchOperation>
				{
					new BatchOperation
					{
						Name = "Cell Separation",
						Description = "Separate cells from culture medium",
						Action = (parameters) =>
						{
							Console.WriteLine("  [Operation] Separating cells...");
						}
					}
				}
			});

			// Create Chromatography Recipe
			var chromRecipe = new Recipe
			{
				RecipeId = Guid.NewGuid().ToString(),
				Name = "Three-Step Purification",
				Version = "1.0",
				Description = "Affinity, ion exchange, and polishing chromatography"
			};

			chromRecipe.Phases.Add(new BatchPhase
			{
				Name = "Affinity Capture",
				Description = "Protein A affinity chromatography",
				Duration = 180,
				Operations = new List<BatchOperation>
				{
					new BatchOperation
					{
						Name = "Load Sample",
						Description = "Load clarified harvest onto Protein A column",
						Action = (parameters) => Console.WriteLine("  [Operation] Loading sample...")
					},
					new BatchOperation
					{
						Name = "Wash",
						Description = "Wash column to remove impurities",
						Action = (parameters) => Console.WriteLine("  [Operation] Washing column...")
					},
					new BatchOperation
					{
						Name = "Elute",
						Description = "Elute bound protein",
						Action = (parameters) => Console.WriteLine("  [Operation] Eluting protein...")
					}
				}
			});

			// Add recipes to recipe manager (accessed through DCS)
			Console.WriteLine($"  - Loaded recipe: {fermRecipe.Name} v{fermRecipe.Version}");
			Console.WriteLine($"  - Loaded recipe: {chromRecipe.Name} v{chromRecipe.Version}\n");
		}

		private async Task RunBatchProductionAsync()
		{
			Console.WriteLine("=== Starting Batch Production ===\n");

			// Start a fermentation batch
			string batchId = $"BATCH-{DateTime.Now:yyyyMMdd}-001";
			var parameters = new Dictionary<string, object>
			{
				{ "TargetVolume", 900.0 },
				{ "TargetTemperature", 37.0 },
				{ "TargetpH", 7.0 },
				{ "FeedStrategy", "Exponential" }
			};

			await _dcs.StartBatchAsync("Standard mAb Fermentation", batchId, parameters);
			Console.WriteLine($"Batch {batchId} started\n");

			// Simulate batch execution for a few seconds
			await Task.Delay(5000);
		}

		private async Task MonitorSystemAsync()
		{
			Console.WriteLine("=== System Monitoring ===\n");

			// Perform health check
			var healthReport = _dcs.PerformHealthCheck();
			Console.WriteLine($"System Health: {healthReport.OverallStatus}");
			Console.WriteLine($"Average Scan Time: {healthReport.AverageScanTime:F2} ms");
			Console.WriteLine($"Control Loops: {healthReport.ControlLoopCount}");
			Console.WriteLine($"Process Units: {healthReport.ProcessUnitCount}");
			Console.WriteLine($"Active Alarms: {healthReport.ActiveAlarmCount}");
			Console.WriteLine($"Uptime: {healthReport.Uptime}\n");

			// Check active alarms
			var alarms = _dcs.GetActiveAlarms();
			if (alarms.Count > 0)
			{
				Console.WriteLine($"Active Alarms ({alarms.Count}):");
				foreach (var alarm in alarms.Take(5))
				{
					Console.WriteLine($"  - [{alarm.Priority}] {alarm.Message}");
				}
			}
			else
			{
				Console.WriteLine("No active alarms");
			}

			await Task.CompletedTask;
		}

		private void GenerateReports()
		{
			Console.WriteLine("\n=== Generating Reports ===\n");

			// Audit Trail Report
			var auditEntries = _dcs.GetAuditTrail(DateTime.Now.AddHours(-1), DateTime.Now);
			Console.WriteLine($"Audit Trail Entries (last hour): {auditEntries.Count}");

			// Control Loop Performance
			var tempLoop = _dcs.GetControlLoop("TC-001");
			if (tempLoop != null)
			{
				var status = ((PIDControlLoop)tempLoop).GetStatus();
				Console.WriteLine($"\nTemperature Control Loop Status:");
				Console.WriteLine($"  Mode: {status.Mode}");
				Console.WriteLine($"  PV: {status.ProcessVariable:F2}");
				Console.WriteLine($"  SP: {status.Setpoint:F2}");
				Console.WriteLine($"  OP: {status.OutputValue:F2}%");
				Console.WriteLine($"  Error: {status.Error:F2}");
			}

			Console.WriteLine($"\nReports generated successfully");
		}

		// Example: Simulating alarm acknowledgement
		public async Task AcknowledgeAlarmExample()
		{
			var alarms = _dcs.GetActiveAlarms();
			if (alarms.Count > 0)
			{
				var alarm = alarms[0];
				bool acknowledged = _dcs.AcknowledgeAlarm(alarm.AlarmId, "OPERATOR-001", "Reviewed and acknowledged");

				if (acknowledged)
				{
					Console.WriteLine($"Alarm {alarm.AlarmId} acknowledged");
				}
			}

			await Task.CompletedTask;
		}

		// Example: Querying historical data
		public async Task QueryHistoricalDataExample()
		{
			var startTime = DateTime.Now.AddHours(-1);
			var endTime = DateTime.Now;

			var tempData = await _dcs.GetHistoricalDataAsync("FERM-01.TE-001.Temperature", startTime, endTime);

			Console.WriteLine($"\nHistorical Temperature Data ({tempData.Count} points):");
			foreach (var point in tempData.Take(10))
			{
				Console.WriteLine($"  {point.Timestamp:HH:mm:ss} - {point.Value:F2}°C");
			}
		}
	}
}

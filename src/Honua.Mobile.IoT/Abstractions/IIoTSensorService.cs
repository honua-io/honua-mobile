// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

namespace Honua.Mobile.IoT.Abstractions;

/// <summary>
/// Revolutionary IoT sensor integration service that no competitor offers.
/// Provides unified interface for connecting to various sensor types across multiple protocols.
/// </summary>
public interface IIoTSensorService
{
    #region Device Discovery & Connection

    /// <summary>
    /// Starts scanning for available sensors of the specified type.
    /// Supports Bluetooth LE, WiFi, and LoRa protocols.
    /// </summary>
    /// <param name="sensorType">Type of sensors to discover</param>
    /// <param name="scanDuration">How long to scan (default: 30 seconds)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when scanning starts</returns>
    Task StartSensorDiscoveryAsync(
        SensorType sensorType,
        TimeSpan? scanDuration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the current sensor discovery process.
    /// </summary>
    Task StopSensorDiscoveryAsync();

    /// <summary>
    /// Gets all discovered sensors that haven't been connected yet.
    /// </summary>
    /// <returns>List of available sensors</returns>
    Task<IReadOnlyList<SensorInfo>> GetDiscoveredSensorsAsync();

    /// <summary>
    /// Connects to a specific sensor by ID.
    /// Handles protocol-specific connection logic automatically.
    /// </summary>
    /// <param name="sensorId">Unique sensor identifier</param>
    /// <param name="timeout">Connection timeout (default: 15 seconds)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Connection result with success status</returns>
    Task<SensorConnectionResult> ConnectToSensorAsync(
        string sensorId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from a sensor.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    Task DisconnectFromSensorAsync(string sensorId);

    /// <summary>
    /// Gets all currently connected sensors.
    /// </summary>
    /// <returns>List of connected sensors</returns>
    Task<IReadOnlyList<ConnectedSensor>> GetConnectedSensorsAsync();

    #endregion

    #region Data Reading & Monitoring

    /// <summary>
    /// Reads the current value from a specific sensor.
    /// Supports both one-time reads and cached values.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    /// <param name="forceRefresh">Whether to force a fresh read vs using cached value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current sensor reading with metadata</returns>
    Task<SensorReading?> ReadSensorAsync(
        string sensorId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts continuous monitoring of a sensor with specified interval.
    /// Readings will be delivered via the SensorDataReceived event.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    /// <param name="interval">Reading interval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartMonitoringSensorAsync(
        string sensorId,
        TimeSpan interval,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops monitoring a specific sensor.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    Task StopMonitoringSensorAsync(string sensorId);

    /// <summary>
    /// Gets historical readings from a sensor within a time range.
    /// Only available for sensors that support local storage.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    /// <param name="startTime">Start of time range</param>
    /// <param name="endTime">End of time range</param>
    /// <param name="maxRecords">Maximum number of records to return</param>
    /// <returns>Historical sensor readings</returns>
    Task<IReadOnlyList<SensorReading>> GetHistoricalReadingsAsync(
        string sensorId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int maxRecords = 1000);

    #endregion

    #region Sensor Configuration & Calibration

    /// <summary>
    /// Gets the current configuration of a sensor.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    /// <returns>Sensor configuration</returns>
    Task<SensorConfiguration?> GetSensorConfigurationAsync(string sensorId);

    /// <summary>
    /// Updates the configuration of a sensor.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    /// <param name="configuration">New configuration</param>
    Task<bool> UpdateSensorConfigurationAsync(string sensorId, SensorConfiguration configuration);

    /// <summary>
    /// Calibrates a sensor using reference values.
    /// Essential for accurate environmental readings.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    /// <param name="calibrationData">Reference calibration data</param>
    /// <returns>Calibration result with success status</returns>
    Task<CalibrationResult> CalibrateSensorAsync(string sensorId, CalibrationData calibrationData);

    /// <summary>
    /// Resets a sensor to factory defaults.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    Task<bool> ResetSensorAsync(string sensorId);

    #endregion

    #region Advanced Features

    /// <summary>
    /// Executes a custom sensor workflow (e.g., multi-step environmental sampling).
    /// Enables complex sensor interactions beyond simple readings.
    /// </summary>
    /// <param name="workflowId">Predefined workflow identifier</param>
    /// <param name="parameters">Workflow parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Workflow execution result with all readings</returns>
    Task<WorkflowResult> ExecuteWorkflowAsync(
        string workflowId,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes sensor data with the Honua server.
    /// Uploads readings and downloads configuration updates.
    /// </summary>
    /// <param name="sensorId">Sensor identifier (null for all sensors)</param>
    /// <returns>Sync result with upload/download counts</returns>
    Task<SyncResult> SyncSensorDataAsync(string? sensorId = null);

    /// <summary>
    /// Gets sensor health and diagnostic information.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    /// <returns>Sensor health status</returns>
    Task<SensorHealth?> GetSensorHealthAsync(string sensorId);

    /// <summary>
    /// Updates sensor firmware if available and supported.
    /// </summary>
    /// <param name="sensorId">Sensor identifier</param>
    /// <param name="progressCallback">Progress callback for firmware update</param>
    /// <returns>Firmware update result</returns>
    Task<FirmwareUpdateResult> UpdateSensorFirmwareAsync(
        string sensorId,
        IProgress<FirmwareUpdateProgress>? progressCallback = null);

    #endregion

    #region Events

    /// <summary>
    /// Fired when a new sensor is discovered during scanning.
    /// </summary>
    event EventHandler<SensorDiscoveredEventArgs> SensorDiscovered;

    /// <summary>
    /// Fired when a sensor connection state changes.
    /// </summary>
    event EventHandler<SensorConnectionEventArgs> SensorConnectionChanged;

    /// <summary>
    /// Fired when new sensor data is received (from monitoring or workflows).
    /// </summary>
    event EventHandler<SensorDataEventArgs> SensorDataReceived;

    /// <summary>
    /// Fired when a sensor error occurs.
    /// </summary>
    event EventHandler<SensorErrorEventArgs> SensorErrorOccurred;

    /// <summary>
    /// Fired when sensor configuration changes.
    /// </summary>
    event EventHandler<SensorConfigurationEventArgs> SensorConfigurationChanged;

    #endregion
}
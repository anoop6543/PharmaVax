namespace PharmaceuticalProcess.HardwareComponents.Controllers
{
	/// <summary>
	/// Represents data integrity compliance levels for pharmaceutical manufacturing systems
	/// </summary>
	public enum DataIntegrityLevel
	{
		/// <summary>
		/// Standard data integrity with basic audit trails
		/// </summary>
		Standard,

		/// <summary>
		/// GAMP5 compliant data integrity
		/// </summary>
		GAMP5,

		/// <summary>
		/// FDA 21 CFR Part 11 compliant electronic records
		/// </summary>
		CFR21Part11,

		/// <summary>
		/// EudraLex Annex 11 compliant electronic records
		/// </summary>
		EudraLex
	}
}
using Ship.Ses.Transmitter.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ship.Ses.Transmitter.Application.Patients.Commands
{
    public class MarkPatientSyncedCommandHandler
    {
        private readonly IPatientWriteService _patientWriteService;

        public MarkPatientSyncedCommandHandler(IPatientWriteService patientWriteService)
        {
            _patientWriteService = patientWriteService;
        }

        public async Task Handle(MarkPatientSyncedCommand command)
        {
            await _patientWriteService.MarkPatientAsSyncedAsync(command.PatientId, command.Status, command.Message);
        }
    }
}

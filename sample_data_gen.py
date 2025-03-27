from pymongo import MongoClient
from datetime import datetime
import uuid

# Connect to your MongoDB server
client = MongoClient("mongodb://localhost:27017")
db = client["shipses"]

# Collections for patient and encounter
patient_collection = db["transformed_pool_patients"]
encounter_collection = db["transformed_pool_encounters"]

# Clean old test data (optional)
patient_collection.delete_many({})
encounter_collection.delete_many({})

def generate_patient_json(index):
    return {
        "resourceType": "Patient",
        "id": str(uuid.uuid4()),
        "name": [{"use": "official", "family": f"TestFamily{index}", "given": [f"TestGiven{index}"]}],
        "gender": "male" if index % 2 == 0 else "female",
        "birthDate": f"198{index}-01-01"
    }

def generate_encounter_json(index):
    return {
        "resourceType": "Encounter",
        "id": str(uuid.uuid4()),
        "status": "finished",
        "class": {
            "system": "http://terminology.hl7.org/CodeSystem/v3-ActCode",
            "code": "AMB",
            "display": "ambulatory"
        },
        "subject": {"reference": f"Patient/TestPatient{index}"},
        "period": {
            "start": f"2024-01-{index+1:02d}T09:00:00Z",
            "end": f"2024-01-{index+1:02d}T10:00:00Z"
        }
    }

# Generate 5 test records each
patients = [{
    "resourceType": "Patient",
    "resourceId": str(uuid.uuid4()),
    "fhirJson": generate_patient_json(i),
    "status": "Pending",
    "createdDate": datetime.utcnow(),
    "timeSynced": None,
    "retryCount": 0,
    "errorMessage": None,
    "syncedFhirResourceId": None
} for i in range(5)]

encounters = [{
    "resourceType": "Encounter",
    "resourceId": str(uuid.uuid4()),
    "fhirJson": generate_encounter_json(i),
    "status": "Pending",
    "createdDate": datetime.utcnow(),
    "timeSynced": None,
    "retryCount": 0,
    "errorMessage": None,
    "syncedFhirResourceId": None
} for i in range(5)]

# Insert into MongoDB
patient_collection.insert_many(patients)
encounter_collection.insert_many(encounters)

print("Inserted 5 patients and 5 encounters into shipses database.")

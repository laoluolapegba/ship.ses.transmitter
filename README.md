# SHIP Edge Server (SeS) - Transmitter Component
🚀 **ship.ses.transmitter**  

## Overview
The **SHIP Edge Server (SeS) - Transmitter Component** (**`ship.ses.transmitter`**) is responsible for **securely transmitting processed healthcare data** from the SHIP Edge Server (SeS) to the **central SHIP platform**. 

It ensures **reliable, secure, and efficient data delivery**, supports **multiple transmission protocols**, and implements **error handling and retry mechanisms** to ensure **data consistency and integrity**.

This service is implemented using **.NET Core**, following **Domain-Driven Design (DDD)** principles.

---

## Features
✅ **Multiple Data Transmission Methods**:
- **gRPC API** (for high-performance communication).
- **REST API (OAuth2.0 JWT Authentication)**.
- **Message Queue (Kafka, RabbitMQ)** for asynchronous data streaming.

✅ Implements **batch and real-time data transmission**.  
✅ Supports **TLS 1.3 encryption** for secure data transmission.  
✅ Implements **error handling and retry logic**.  
✅ Logs **transmission events and failures** for debugging and compliance.  
✅ Designed following **Domain-Driven Design (DDD) principles**.  

---

## Repository Structure (Domain-Driven Design)
```
ship.ses.transmitter/
│── src/
│   ├── Ship.Ses.Transmitter.Api/          # API layer for external communications
│   ├── Ship.Ses.Transmitter.Application/  # Application Services (Use Cases, Command Handlers)
│   ├── Ship.Ses.Transmitter.Domain/       # Domain Layer (Entities, Aggregates, Domain Services)
│   ├── Ship.Ses.Transmitter.Infrastructure/ # Infrastructure Layer (Persistence, External Integrations)
│   ├── Ship.Ses.Transmitter.Worker/       # Background worker service for scheduled transmissions
│── tests/
│   ├── Ship.Ses.Transmitter.UnitTests/    # Unit tests for domain & application logic
│   ├── Ship.Ses.Transmitter.IntegrationTests/ # Integration tests for API & queue interactions
│── docker-compose.yml
│── README.md
│── .gitignore
│── LICENSE
│── Ship.Ses.Transmitter.sln
```

---

## Installation
### **Prerequisites**
- **.NET 8.0+**
- **Docker** (for containerized deployments)
- **gRPC & REST API support**
- **RabbitMQ / Kafka** (for message queuing)

### **Clone the Repository**
```sh
git clone https://github.com/your-org/ship.ses.transmitter.git
cd ship.ses.transmitter
```

### **Setup Configuration**
- Copy `.env.example` to `.env` and configure your environment variables:
  ```sh
  cp .env.example .env
  ```

- Edit `.env` with your preferred settings:
  ```ini
  SHIP_GRPC_URL="ship.platform.com:50051"
  SHIP_API_URL="https://api.ship.platform.com"
  MESSAGE_QUEUE="kafka://ship.platform.com:9092"
  ```

---

## Running the Application
### **Run with Docker**
```sh
docker-compose up --build
```

### **Run Locally**
1. Restore dependencies:
   ```sh
   dotnet restore
   ```
2. Build the solution:
   ```sh
   dotnet build
   ```
3. Run the background worker:
   ```sh
   dotnet run --project src/Ship.Ses.Transmitter.Worker
   ```

---

## Configuration
The application is configured using **`appsettings.json`** and supports **environment-based configurations**.

Example `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Error"
    }
  },
  "Transmission": {
    "Method": "gRPC", // Options: gRPC, REST, Kafka
    "RetryCount": 3,
    "BatchSize": 10
  },
  "ApiSettings": {
    "GrpcUrl": "ship.platform.com:50051",
    "RestApiBaseUrl": "https://api.ship.platform.com"
  }
}
```

---

## Authentication
All API requests require **OAuth2 Bearer Tokens**.

### **Example Authorization Header**
```http
Authorization: Bearer <ACCESS_TOKEN>
```

---

## Logging & Monitoring
SHIP Mini logs events using **Serilog**, and all logs are forwarded to **ELK Stack (Elasticsearch, Logstash, Kibana)**.

### **Log Example**
```json
{
  "timestamp": "2025-02-15T12:45:00Z",
  "level": "Information",
  "message": "Data transmission successful",
  "context": {
    "method": "gRPC",
    "status": "success",
    "transmissionId": "txn-abc123"
  }
}
```

---

## Testing
Run **unit tests**:
```sh
dotnet test
```
Run **integration tests**:
```sh
dotnet test tests/Ship.Ses.Transmitter.IntegrationTests
```

---

## Deployment
**Kubernetes Helm Chart Deployment**
```sh
helm upgrade --install ses-transmitter charts/ses-transmitter
```

**Azure Deployment (Using ACR & AKS)**
```sh
az acr build --image ses-transmitter:v1.0 --registry mycontainerregistry .
az aks deploy --name ses-transmitter --image mycontainerregistry/ses-transmitter:v1.0
```

---

## License
📜 **MIT License** – Open-source for community and enterprise use.

---

## Contacts & Support
- 📧 **Support**: support@ses.io
- 🚀 **Contributors**: @yourteam  
- 📚 **Docs**: [Confluence Page](https://confluence.ses.io/docs)

---

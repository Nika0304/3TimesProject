# Serverul 3Times. Se compilează direct în Docker: pe laptop nu e nevoie de Java sau Maven.

# 1. Compilare (Maven + Java 17)
FROM maven:3.9-eclipse-temurin-17 AS build
WORKDIR /src
COPY pom.xml .
RUN mvn -q -B dependency:go-offline
COPY src ./src
RUN mvn -q -B package -DskipTests

# 2. Rulare (doar Java 17, imagine mică)
FROM eclipse-temurin:17-jre
WORKDIR /app
COPY --from=build /src/target/threetimes-backend.jar app.jar
EXPOSE 8080
ENTRYPOINT ["java", "-XX:MaxRAMPercentage=75", "-jar", "app.jar"]

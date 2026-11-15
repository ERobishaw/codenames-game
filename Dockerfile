FROM node:22-alpine AS frontend
WORKDIR /app/client
COPY client/package*.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS backend
WORKDIR /app
COPY server/server.csproj server/
RUN dotnet restore server/server.csproj
COPY server/ server/
RUN dotnet publish server/server.csproj -c Release -o /publish --no-restore
COPY --from=frontend /app/client/dist/ /publish/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=backend /publish/ ./
USER $APP_UID
EXPOSE 10000
CMD ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-10000} dotnet server.dll"]

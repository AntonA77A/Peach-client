# Peach Client

Peach Client is a small personal Windows tool. One part of it is a **launcher for Minecraft: Java Edition**: it manages profiles and mods and starts the game for the owner of a Minecraft account.

This page describes the Minecraft launcher part, because it uses the Minecraft services API.

> Not an official Minecraft product. Not approved by or associated with Mojang or Microsoft.

## What the launcher does

- **Profiles:** every profile has its own game folder (mods, worlds, settings). Game files are shared between profiles.
- **Game files:** downloaded from Mojang's own servers (`piston-meta.mojang.com`, `piston-data.mojang.com`, `libraries.minecraft.net`, `resources.download.minecraft.net`). Every file is checked against the SHA-1 checksum from Mojang's version files. Nothing is downloaded from other hosts.
- **Fabric:** the Fabric loader is set up from `meta.fabricmc.net` / `maven.fabricmc.net`.
- **Mods:** search and install from Modrinth (`api.modrinth.com`, `cdn.modrinth.com`). Downloads are checked against the SHA-512 checksum Modrinth publishes.
- **Start:** the launcher finds a matching Java installation and starts the game with the usual arguments.

## Sign-in and the Minecraft services API

The launcher signs in the player with **Microsoft's device code flow**:

1. The player opens `https://www.microsoft.com/link` and enters a code. The launcher never sees the password.
2. Standard chain: Microsoft token → Xbox Live → XSTS → `api.minecraftservices.com/authentication/login_with_xbox` → `/minecraft/profile`.
3. The access token is only used to start the game and is kept in memory. The refresh token is stored on the user's own PC, encrypted with Windows DPAPI (readable only by the same Windows user).

The only data used from the Minecraft services API is the player's name and UUID, to start the game.

## Privacy and security

- No telemetry, no analytics, no accounts of its own, no server run by this project.
- No user data is sent anywhere except to Microsoft / Xbox / Mojang (sign-in) and to the download hosts listed above.
- Access tokens are redacted from the game log.
- Mods are never installed without the player choosing them.

## Scope

Personal, non-commercial use by the author and a few friends. No ads, no payments, no monetisation.

## Application registration

- Application name: **Peach Client**
- Application (client) ID: `44759093-1f79-447a-8e40-5d6a39a472ec`
- Account types: personal Microsoft accounts, public client (device code flow, no client secret)

## Contact

Questions about this application: open an issue in this repository.

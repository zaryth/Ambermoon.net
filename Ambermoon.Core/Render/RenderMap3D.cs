﻿/*
 * RenderMap3D.cs - Handles 3D map rendering
 *
 * Copyright (C) 2020-2021  Robert Schneckenhaus <robert.schneckenhaus@web.de>
 *
 * This file is part of Ambermoon.net.
 *
 * Ambermoon.net is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * Ambermoon.net is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with Ambermoon.net. If not, see <http://www.gnu.org/licenses/>.
 */

using Ambermoon;
using Ambermoon.Data;
using Ambermoon.Data.Enumerations;
using Ambermoon.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using static Ambermoon.Data.Map.CharacterReference;

namespace Ambermoon.Render
{
    internal class RenderMap3D
    {
        class MapObject
        {
            readonly RenderMap3D map;
            readonly ISurface3D surface;
            readonly uint objectIndex;
            readonly uint numFrames;
            readonly uint ticksPerFrame;
            readonly bool alternateAnimation;
            bool animateForward = true;

            public MapObject(RenderMap3D map, ISurface3D surface,
                uint objectIndex, bool alternateAnimation,
                uint numFrames, float fps = 1.0f)
            {
                this.surface = surface;
                this.map = map;
                this.objectIndex = objectIndex;
                this.alternateAnimation = alternateAnimation;
                this.numFrames = numFrames;
                ticksPerFrame = Math.Max(1, (uint)Util.Round(Game.TicksPerSecond / Math.Max(0.001f, fps)));
            }

            public void Destroy()
            {
                surface?.Delete();
            }

            public void Update(uint ticks)
            {
                if (numFrames <= 1 || !surface.Visible)
                    return;

                uint frame = ticks / ticksPerFrame;

                if (alternateAnimation)
                {
                    if (animateForward && (frame / numFrames) % 2 == 1)
                        animateForward = false;
                    else if (!animateForward && (frame / numFrames) % 2 == 0)
                        animateForward = true;
                    frame %= numFrames;
                    if (!animateForward)
                        frame = numFrames - frame - 1;
                }
                else
                    frame %= numFrames;
                surface.TextureAtlasOffset = map.GetObjectTextureOffset(objectIndex) +
                    new Position((int)(frame * surface.TextureWidth), 0);
            }
        }

        class MapCharacter : IMapCharacter
        {
            readonly Game game;
            readonly RenderMap3D map;
            readonly ISurface3D surface;
            readonly uint characterIndex;
            readonly uint numFrames;
            readonly uint ticksPerFrame;
            readonly Map.CharacterReference characterReference;
            readonly uint textureIndex;
            readonly Labdata.ObjectPosition objectPosition;
            readonly bool alternateAnimation;
            bool active = true;
            bool animateForward = true;
            uint lastInteractionTicks = 0;
            // This is used to avoid multiple monster encounters in the same update frame (e.g. 2 monsters move onto the player at the same time).
            static bool interacting = false;
            readonly Character3D character3D;
            readonly List<MapCharacter> children = new List<MapCharacter>(7);
            readonly MapCharacter parent = null;
            public Tileset.TileFlags TileFlags => characterReference?.TileFlags ?? Tileset.TileFlags.None;
            public CharacterType? Type => characterReference?.Type;
            public uint EventId => characterReference?.EventIndex ?? 0;
            public uint MapObjectIndex => characterReference?.GraphicIndex ?? 0;

            public static void Reset() => interacting = false;

            public void ResetLastInteractionTime() => lastInteractionTicks = game.CurrentTicks;

            public MapCharacter(Game game, RenderMap3D map, ISurface3D surface,
                uint characterIndex, Map.CharacterReference characterReference,
                Labdata.ObjectPosition objectPosition, uint textureIndex, MapCharacter parent,
                bool alternateAnimation, uint numFrames, float fps = 1.0f)
            {
                this.game = game;
                this.surface = surface;
                this.map = map;
                this.characterIndex = characterIndex;
                this.numFrames = numFrames;
                ticksPerFrame = Math.Max(1, (uint)Util.Round(Game.TicksPerSecond / Math.Max(0.001f, fps)));
                this.characterReference = characterReference;
                this.textureIndex = textureIndex;
                this.objectPosition = objectPosition;
                this.alternateAnimation = alternateAnimation;
                this.parent = parent;
                if (parent != null)
                    character3D = parent.character3D;
                else
                {
                    character3D = new Character3D(game);
                    character3D.RandomMovementRequested += MoveRandom;
                    character3D.MoveRequested += TestPossibleCharacterMovement;
                    ResetPosition(game.GameTime);
                }
            }

            public ICollisionBody GetCollisionBody()
            {
                return new CollisionSphere3D
                {
                    CenterX = surface.X,
                    CenterZ = -surface.Z,
                    Radius = Global.DistancePerBlock * objectPosition.Object.MappedTextureWidth / BlockSize
                };
            }

            public void AddChild(MapCharacter child)
            {
                children.Add(child);
            }

            public void Destroy()
            {
                children.ForEach(c => c?.Destroy());
                children.Clear();
                surface?.Delete();
            }

            public bool Active
            {
                get => parent?.Active ?? active;
                set
                {
                    if (parent != null)
                        return;

                    if (active == value)
                        return;

                    active = value;
                    surface.Visible = active;
                    children.ForEach(c =>
                    {
                        c.active = active;
                        c.surface.Visible = active;
                    });
                }
            }

            public void Pause()
            {
                if (parent == null)
                    character3D.Paused = true;
            }

            public void Resume()
            {
                if (parent == null)
                    character3D.Paused = false;
            }

            public Position Position
            {
                get => character3D.Position;
                set
                {
                    character3D.Place((uint)value.X, (uint)value.Y, false);
                    UpdatePosition();
                }
            }

            public void ResetPosition(ITime gameTime)
            {
                var position = new Position(characterReference.Positions[0]);
                position.Offset(-1, -1); // positions are 1-based
                Position = position;
                ResetFrame();
            }

            void ResetFrame()
            {
                uint frame = numFrames / 2;
                surface.TextureAtlasOffset = map.GetObjectTextureOffset(textureIndex) +
                    new Position((int)(frame * surface.TextureWidth), 0);
            }

            float GetDistance(float x1, float y1, float x2, float y2)
            {
                float diffX = x2 - x1;
                float diffY = y2 - y1;
                return (float)Math.Sqrt(diffX * diffX + diffY * diffY);
            }

            public bool CheckDeactivation(uint deactivatedEventIndex)
            {
                if (characterReference.EventIndex == deactivatedEventIndex)
                {
                    if (Active && characterReference.Type == CharacterType.MapObject)
                        Deactivate();
                    return true;
                }

                return false;
            }

            void Deactivate()
            {
                Active = false;
                game.CurrentSavegame.SetCharacterBit(map.Map.Index, characterIndex, true);

                if (game.CurrentMapCharacter == this)
                    game.CurrentMapCharacter = null;
            }

            public bool Interact(EventTrigger trigger, bool bed)
            {
                if (parent != null)
                    return false;

                game.CurrentMapCharacter = null;

                bool TriggerCharacterEvents(uint eventIndex)
                {
                    if ((long)game.CurrentTicks - lastInteractionTicks < Game.TicksPerSecond)
                        return false;

                    var @event = map.Map.EventList[(int)eventIndex - 1];

                    if (@event is ConditionEvent conditionEvent)
                    {
                        switch (conditionEvent.TypeOfCondition)
                        {
                            case ConditionEvent.ConditionType.Eye:
                                if (trigger != EventTrigger.Eye)
                                    return false;
                                @event = conditionEvent.Next;
                                trigger = EventTrigger.Always;
                                break;
                            case ConditionEvent.ConditionType.Hand:
                                if (trigger != EventTrigger.Hand)
                                    return false;
                                @event = conditionEvent.Next;
                                trigger = EventTrigger.Always;
                                break;
                            case ConditionEvent.ConditionType.UseItem:
                            {
                                if (trigger < EventTrigger.Item0)
                                    return false;
                                var itemIndex = (uint)trigger - (uint)EventTrigger.Item0;
                                if (conditionEvent.ObjectIndex != itemIndex)
                                    return false;
                                @event = conditionEvent.Next;
                                trigger = EventTrigger.Always;
                                break;
                            }
                        }
                    }
                    lastInteractionTicks = uint.MaxValue;
                    interacting = true;
                    game.CurrentMapCharacter = this;
                    var position = game.RenderPlayer.Position;
                    return EventExtensions.TriggerEventChain(map.Map, game, trigger, (uint)position.X, (uint)position.Y, game.CurrentTicks, @event, true);
                }

                if (characterReference.Type == CharacterType.Monster)
                {
                    if (trigger == EventTrigger.Move)
                    {
                        if ((long)game.CurrentTicks - lastInteractionTicks < Game.TicksPerSecond)
                            return false;

                        // First set this to max so we won't trigger this again while we are interacting.
                        lastInteractionTicks = uint.MaxValue;
                        interacting = true;
                        game.CurrentMapCharacter = this;

                        // Turn the player towards the monster.
                        var player3D = game.RenderPlayer as Player3D;
                        player3D.TurnTowards(character3D.RealPosition);
                        Geometry.Geometry.CameraToMapPosition(map.Map, player3D.Camera.X, player3D.Camera.Z, out float mapX, out float mapY);
                        var playerPosition = new FloatPosition(mapX - 0.5f * Global.DistancePerBlock, mapY - 0.5f * Global.DistancePerBlock);
                        var distance = GetDistance(playerPosition.X, playerPosition.Y, character3D.RealPosition.X, character3D.RealPosition.Y);
                        float extrude = surface.Extrude = (-BlockSize / 10.0f) * Math.Max(0.0f, 1.0f - distance) * Global.DistancePerBlock / BlockSize;
                        children.ForEach(c =>
                        {
                            extrude -= ExtrudeStep;
                            c.surface.Extrude = extrude;
                        });
                        void RestoreExtrude()
                        {
                            float extrude = surface.Extrude = 8.0f * ExtrudeStep;
                            children.ForEach(c =>
                            {
                                extrude -= ExtrudeStep;
                                c.surface.Extrude = extrude;
                            });
                        }
                        void StartBattle(bool failedEscape)
                        {
                            game.StartBattle(characterReference.Index, failedEscape, battleEndInfo =>
                            {
                                lastInteractionTicks = game.CurrentTicks;
                                interacting = false;
                                game.CurrentMapCharacter = null;

                                if (battleEndInfo.MonstersDefeated)
                                {
                                    Deactivate();
                                }
                                else
                                {
                                    RestoreExtrude();
                                    character3D.ResetMovementTimer();
                                }
                            }, characterReference.CombatBackgroundIndex);
                        }
                        game.ShowDecisionPopup(game.DataNameProvider.WantToFightMessage, response =>
                        {
                            if (response == PopupTextEvent.Response.Yes)
                            {
                                StartBattle(false);
                            }
                            else
                            {
                                var attributes = game.CurrentPartyMember.Attributes;
                                var dex = attributes[Data.Attribute.Dexterity].TotalCurrentValue;
                                var luk = attributes[Data.Attribute.Luck].TotalCurrentValue;
                                if (game.RandomInt(0, 149) >= dex + luk)
                                {
                                    StartBattle(true);
                                }
                                else
                                {
                                    // successfully fled
                                    RestoreExtrude();
                                    lastInteractionTicks = game.CurrentTicks;
                                    interacting = false;
                                    game.CurrentMapCharacter = null;
                                    character3D.ResetMovementTimer();
                                }
                            }
                        }, 2, 0, TextAlign.Left, false);

                        return true;
                    }
                }
                else
                {
                    if (characterReference.CharacterFlags.HasFlag(Flags.TextPopup))
                    {
                        if (characterReference.EventIndex != 0 && game.CurrentSavegame.IsEventActive(map.Map.Index, characterReference.EventIndex - 1))
                        {
                            return TriggerCharacterEvents(characterReference.EventIndex);
                        }
                        else if (trigger == EventTrigger.Eye)
                        {
                            // Popup NPCs can't be looked at but only talked to.
                            return false;
                        }
                        else if (trigger == EventTrigger.Mouth)
                        {
                            ShowPopup(map.Map.Texts[(int)characterReference.Index]);
                            return true;
                        }

                        return false;
                    }

                    bool HandleConversation(IConversationPartner conversationPartner)
                    {
                        if (trigger == EventTrigger.Eye)
                        {
                            game.ShowTextPopup(game.ProcessText(conversationPartner.Texts[0]), null);
                            return true;
                        }
                        else if (trigger == EventTrigger.Mouth)
                        {
                            if (conversationPartner == null)
                                throw new AmbermoonException(ExceptionScope.Data, "Invalid NPC or party member index.");

                            (conversationPartner as Character).CharacterBitIndex = (ushort)(((map.Map.Index - 1) << 5) | characterIndex);

                            conversationPartner.ExecuteEvents(game, trigger);
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    switch (characterReference.Type)
                    {
                        case CharacterType.PartyMember:
                            return HandleConversation(game.CurrentSavegame.PartyMembers[characterReference.Index]);
                        case CharacterType.NPC:
                            return HandleConversation(game.CharacterManager.GetNPC(characterReference.Index));
                        case CharacterType.MapObject:
                            if (characterReference.EventIndex != 0 && game.CurrentSavegame.IsEventActive(map.Map.Index, characterReference.EventIndex - 1))
                                return TriggerCharacterEvents(characterReference.EventIndex);
                            break;
                    }
                }

                return false;
            }

            void ShowPopup(string text)
            {
                game.ShowTextPopup(game.ProcessText(text), null);
            }

            void UpdatePosition()
            {
                map.UpdateCharacterSurfaceCoordinates(character3D.RealPosition, surface, objectPosition);
            }

            void UpdateCurrentMovement(uint ticks)
            {
                if (!character3D.Moving && characterReference.Type != CharacterType.MapObject)
                    ResetFrame();
                else
                {
                    if (surface.Visible && numFrames > 1)
                    {
                        uint frame = ticks / ticksPerFrame;

                        if (alternateAnimation)
                        {
                            if (animateForward && (frame / numFrames) % 2 == 1)
                                animateForward = false;
                            else if (!animateForward && (frame / numFrames) % 2 == 0)
                                animateForward = true;
                            frame %= numFrames;
                            if (!animateForward)
                                frame = numFrames - frame - 1;
                        }
                        else
                            frame %= numFrames;

                        surface.TextureAtlasOffset = map.GetObjectTextureOffset(textureIndex) +
                            new Position((int)(frame * surface.TextureWidth), 0);
                    }
                }

                UpdatePosition();

                children.ForEach(c => c.UpdateCurrentMovement(ticks));
            }

            bool TestPathCollision(FloatPosition position, List<uint> blockingTiles)
            {
                var testPosition = Position * Global.DistancePerBlock;

                while (true)
                {
                    if (testPosition.GetMaxDistance(position) < Global.DistancePerBlock / 4)
                        return false;

                    var distance = position - testPosition;

                    if (distance.X < -Global.DistancePerBlock / 8)
                        testPosition.X -= Global.DistancePerBlock / 4;
                    else if (distance.X > Global.DistancePerBlock / 8)
                        testPosition.X += Global.DistancePerBlock / 4;

                    if (distance.Y < -Global.DistancePerBlock / 8)
                        testPosition.Y -= Global.DistancePerBlock / 4;
                    else if (distance.Y > Global.DistancePerBlock / 8)
                        testPosition.Y += Global.DistancePerBlock / 4;

                    var roundedTestPosition = testPosition.Round(1.0f / Global.DistancePerBlock);
                    uint blockIndex = (uint)(roundedTestPosition.X + roundedTestPosition.Y * map.Map.Width);

                    if (blockingTiles.Contains(blockIndex))
                        return true;
                }
            }

            bool CanSee(FloatPosition position)
            {
                return !TestPathCollision(position, map.monsterBlockSightBlocks);
            }

            bool TestPossibleCharacterMovement(FloatPosition position)
            {
                // Note: This is only used for monsters.
                var collisionInfo = map.GetCollisionDetectionInfoForMonsterFromPositions
                (
                    Position,
                    position.Round(1.0f / Global.DistancePerBlock)
                );

                var lastX = character3D.RealPosition.X;
                var lastY = map.Map.Height * Global.DistancePerBlock - character3D.RealPosition.Y;
                var newX = position.X;
                var newY = map.Map.Height * Global.DistancePerBlock - position.Y;

                return !collisionInfo.TestCollision(lastX, lastY, newX, newY, Player3D.CollisionRadius, false);
            }

            void MoveRandom()
            {
                Position newPosition = null;

                for (int i = 0; i < 10; ++i)
                {
                    newPosition = new Position(Position.X + game.RandomInt(-1, 1), Position.Y + game.RandomInt(-1, 1));

                    if (newPosition == Position)
                        continue;

                    var collisionPosition = new Position(newPosition.X, newPosition.Y);

                    if (collisionPosition.X < 0 || collisionPosition.X >= map.Map.Width)
                        continue;

                    if (collisionPosition.Y < 0 || collisionPosition.Y >= map.Map.Height)
                        continue;

                    uint blockIndex = (uint)(newPosition.X + newPosition.Y * map.Map.Width);

                    if (!map.characterBlockingBlocks.Contains(blockIndex) &&
                        !map.EventBlocksCharacter(newPosition))
                        break;
                }

                if (newPosition != null)
                    character3D.MoveToTile((uint)newPosition.X, (uint)newPosition.Y);
            }

            public void Update(uint ticks, ITime gameTime)
            {
                if (!Active || character3D.Paused || parent != null)
                    return;

                var camera = (game.RenderPlayer as Player3D).Camera;
                Geometry.Geometry.CameraToMapPosition(map.Map, camera.X, camera.Z, out float mapX, out float mapY);
                var playerPosition = new FloatPosition(mapX - 0.5f * Global.DistancePerBlock, mapY - 0.5f * Global.DistancePerBlock);
                var distance = GetDistance(playerPosition.X, playerPosition.Y, character3D.RealPosition.X, character3D.RealPosition.Y) / Global.DistancePerBlock;
                var obj = map.labdata.Objects[(int)characterReference.GraphicIndex - 1];
                var subObject = obj.SubObjects[0];
                var monsterRadius = 0.5f * subObject.Object.MappedTextureWidth / BlockSize;

                if (distance - monsterRadius < 0.5f)
                {
                    if (characterReference.Type == CharacterType.Monster)
                    {
                        // Monster has reached player -> interact/fight
                        game.MonsterSeesPlayer = true;
                        character3D.Stop(true);
                    }
                    if (!interacting && Interact(EventTrigger.Move, false))
                        return;
                }

                bool randomMovement = characterReference.CharacterFlags.HasFlag(Flags.RandomMovement);

                if (!randomMovement && characterReference.Type != CharacterType.Monster)
                {
                    // Walk a given path every day time slot
                    uint lastTimeSlot = gameTime.TimeSlot == 0 ? 287 : gameTime.TimeSlot - 1;
                    var lastPosition = new Position(characterReference.Positions[(int)lastTimeSlot]);
                    var newPosition = new Position(characterReference.Positions[(int)gameTime.TimeSlot]);
                    newPosition.Offset(-1, -1); // positions are 1-based
                    lastPosition.Offset(-1, -1);
                    character3D.MoveToTile((uint)newPosition.X, (uint)newPosition.Y, lastPosition);
                }

                bool monster = characterReference.Type == CharacterType.Monster;
                bool canSeePlayer = (monster || characterReference.OnlyMoveWhenSeePlayer) && CanSee(playerPosition);

                if (monster && canSeePlayer)
                    game.MonsterSeesPlayer = true;

                character3D.Update(ticks, playerPosition, randomMovement, canSeePlayer,
                    characterReference.OnlyMoveWhenSeePlayer, monster);

                UpdateCurrentMovement(ticks);
            }
        }

        /// <summary>
        /// Scrollable texture sprite used for skies.
        /// </summary>
        class SkySprite
        {
            ILayerSprite leftSprite;
            ILayerSprite rightSprite;

            public SkySprite(int y, Func<int, int, ILayerSprite> creator)
            {
                leftSprite = creator(Global.Map3DViewX, Global.Map3DViewY + y);
                rightSprite = creator(Global.Map3DViewX + leftSprite.Width, Global.Map3DViewY + y);
                leftSprite.Visible = true;
                rightSprite.Visible = true;
            }

            public void Destroy()
            {
                leftSprite?.Delete();
                rightSprite?.Delete();

                leftSprite = null;
                rightSprite = null;
            }

            public void ScrollTo(int x)
            {
                if (leftSprite == null || rightSprite == null)
                    return;

                while (x <= -Global.Map3DViewWidth)
                    x += Global.Map3DViewWidth;
                while (x > 0)
                    x -= Global.Map3DViewWidth;

                leftSprite.X = Global.Map3DViewX + x;
                rightSprite.X = leftSprite.X + leftSprite.Width;
            }
        }

        public const int FloorTextureWidth = 64;
        public const int FloorTextureHeight = 64;
        public const int TextureWidth = 128;
        public const int TextureHeight = 80;
        public const float BlockSize = 512.0f;
        public const float ReferenceWallHeight = 341.0f; // 2/3 of block size -> 512
        // Each of the 8 subobjects is sorted in Z (the first has an extrude of 5% of the block size, the rest is lower).
        const float ExtrudeStep = 0.05f * Global.DistancePerBlock * 0.125f;
        readonly Game game;
        readonly ICamera3D camera = null;
        readonly IMapManager mapManager = null;
        readonly IRenderView renderView = null;
        ITextureAtlas textureAtlas = null;
        IColoredRect floorColor = null;
        IColoredRect ceilingColor = null;
        List<IColoredRect> skyColors = null;
        readonly List<KeyValuePair<Position, IColoredRect>> stars = new List<KeyValuePair<Position, IColoredRect>>();
        SkySprite horizonSprite = null;
        ISurface3D floor = null;
        ISurface3D ceiling = null;
        Labdata labdata = null;
        readonly List<uint> playerBlockingBlocks = new List<uint>();
        readonly List<uint> characterBlockingBlocks = new List<uint>();
        readonly List<uint> monsterBlockSightBlocks = new List<uint>();
        readonly Dictionary<uint, List<ICollisionBody>> blockCollisionBodies = new Dictionary<uint, List<ICollisionBody>>();
        readonly Dictionary<uint, List<ISurface3D>> walls = new Dictionary<uint, List<ISurface3D>>();
        readonly Dictionary<uint, List<MapObject>> objects = new Dictionary<uint, List<MapObject>>();
        readonly Dictionary<uint, MapCharacter> mapCharacters = new Dictionary<uint, MapCharacter>();
        static readonly Dictionary<uint, ITextureAtlas> labdataTextures = new Dictionary<uint, ITextureAtlas>(); // contains all textures for a labdata (floor, walls, objects and overlays)
        static Graphic[] labBackgroundGraphics = null;
        public uint CombatBackgroundIndex => labdata.CombatBackground;
        /// <summary>
        /// This contains all block indices that could be changed by map events for labdatas.
        /// </summary>
        static readonly Dictionary<uint, List<uint>> labdataChangeableBlocks = new Dictionary<uint, List<uint>>();
        public Map Map { get; private set; } = null;
        /// <summary>
        ///  This is the height for the renderer. It is expressed in relation
        ///  to the block size (e.g. wall is 2/3 as height as a block is wide).
        /// </summary>
        float WallHeight => labdata.WallHeight * Global.DistancePerBlock / BlockSize;
        public event Action<Map> MapChanged;

        public static void Reset() => MapCharacter.Reset();

        public CharacterType? CharacterTypeFromBlock(uint x, uint y, out AutomapType automapType)
        {
            automapType = AutomapType.None;
            var character = mapCharacters.Values.FirstOrDefault(c => c.Active && c.Position.X == x && c.Position.Y == y);

            if (character?.Type == CharacterType.MapObject)
            {
                if (character.MapObjectIndex != 0)
                {
                    var mapObject = labdata.Objects[(int)character.MapObjectIndex - 1];
                    automapType = mapObject.AutomapType;
                }
            }
            else if (character != null)
            {
                automapType = character.Type == CharacterType.Monster ? AutomapType.Monster : AutomapType.Person;
            }

            return character?.Type;
        }

        public RenderMap3D(Game game, Map map, IMapManager mapManager, IRenderView renderView, uint playerX, uint playerY, CharacterDirection playerDirection)
        {
            this.game = game;
            camera = renderView.Camera3D;
            this.mapManager = mapManager;
            this.renderView = renderView;

            EnsureLabBackgroundGraphics(renderView.GraphicProvider);

            // Create stars
            var starLayer = renderView.GetLayer(Layer.Map3DBackground);
            int x = 0;
            var random = new Random();
            const int starAreaWidth = 8 * Global.Map3DViewWidth;
            for (int y = 4; y < 36; ++y)
            {
                x %= starAreaWidth;

                while (x < starAreaWidth)
                {
                    var star = renderView.ColoredRectFactory.Create(1, 1, Color.White, 2);
                    star.X = Global.Map3DViewX + x;
                    star.Y = Global.Map3DViewY + y;
                    star.Layer = starLayer;
                    star.ClipArea = Game.Map3DViewArea;
                    star.Visible = false;
                    stars.Add(KeyValuePair.Create(new Position(x, y), star));
                    x += starAreaWidth * 2 / 3 + (int)(random.Next() % (starAreaWidth / 3));
                }
            }

            if (map != null)
                SetMap(map, playerX, playerY, playerDirection, game.CurrentPartyMember?.Race ?? Race.Human);

            camera.Turned += CameraTurned;
        }

        void CameraTurned(float angle)
        {
            while (angle <= -360.0f)
                angle += 360.0f;
            while (angle >= 360.0f)
                angle -= 360.0f;

            int scrollX = Util.Round(8.0f * -144.0f * angle / 360.0f);

            if (horizonSprite != null)
                horizonSprite.ScrollTo(scrollX);

            UpdateStars(scrollX);
        }

        void SetupBackground()
        {
            floorColor = renderView.ColoredRectFactory.Create(Global.Map3DViewWidth, Global.Map3DViewHeight / 2,
                game.GetPaletteColor((byte)Map.PaletteIndex, labdata.FloorColorIndex), 0);
            ceilingColor = renderView.ColoredRectFactory.Create(Global.Map3DViewWidth, Global.Map3DViewHeight / 2,
                game.GetPaletteColor((byte)Map.PaletteIndex, labdata.CeilingColorIndex), 0);

            floorColor.X = Global.Map3DViewX;
            floorColor.Y = Global.Map3DViewY + ceilingColor.Height;
            ceilingColor.X = Global.Map3DViewX;
            ceilingColor.Y = Global.Map3DViewY;

            floorColor.Layer = ceilingColor.Layer = renderView.GetLayer(Layer.Map3DBackground);
            floorColor.Visible = ceilingColor.Visible = true;

            if (Map.Flags.HasFlag(MapFlags.Outdoor))
            {
                horizonSprite = new SkySprite(ceilingColor.Height - 20, (x, y) =>
                {
                    var sprite = renderView.SpriteFactory.Create(144, 20, true, 2) as ILayerSprite;
                    sprite.TextureAtlasOffset = HorizonTextureOffset;
                    sprite.ClipArea = Game.Map3DViewArea;
                    sprite.PaletteIndex = (byte)(Map.PaletteIndex - 1);
                    sprite.Layer = ceilingColor.Layer;
                    sprite.X = x;
                    sprite.Y = y;
                    return sprite;
                });
            }
        }

        void SetColors(PaletteReplacement paletteReplacement)
        {
            floorColor.Visible = ceilingColor.Visible = game.CanSee();

            if (paletteReplacement != null)
            {
                int floorIndex = labdata.FloorColorIndex * 4;
                byte fr = paletteReplacement.ColorData[floorIndex + 0];
                byte fg = paletteReplacement.ColorData[floorIndex + 1];
                byte fb = paletteReplacement.ColorData[floorIndex + 2];
                int ceilingIndex = labdata.CeilingColorIndex * 4;
                byte cr = paletteReplacement.ColorData[ceilingIndex + 0];
                byte cg = paletteReplacement.ColorData[ceilingIndex + 1];
                byte cb = paletteReplacement.ColorData[ceilingIndex + 2];
                floorColor.Color = new Color(fr, fg, fb);
                ceilingColor.Color = new Color(cr, cg, cb);
            }
            else
            {
                floorColor.Color = game.GetPaletteColor((byte)Map.PaletteIndex, labdata.FloorColorIndex);
                ceilingColor.Color = game.GetPaletteColor((byte)Map.PaletteIndex, labdata.CeilingColorIndex);
            }
        }

        public void SetMap(Map map, uint playerX, uint playerY, CharacterDirection playerDirection, Race race)
        {
            if (map.Type != MapType.Map3D)
                throw new AmbermoonException(ExceptionScope.Application, "Tried to load a 2D map into a 3D render map.");

            if (Map != map)
            {
                Destroy();

                Map = map;
                labdata = mapManager.GetLabdataForMap(map);
                EnsureLabdataTextureAtlas();
                EnsureChangeableBlocks();
                UpdateSurfaces();
                SetupBackground();
                AddCharacters();

                SetCameraHeight(race);

                renderView.AspectProcessor?.Invoke(ReferenceWallHeight / labdata.WallHeight);

                MapChanged?.Invoke(map);
            }

            camera.SetPosition(playerX * Global.DistancePerBlock, (map.Height - playerY) * Global.DistancePerBlock);
            camera.TurnTowards((float)playerDirection * 90.0f);
        }

        public float GetFloorY() => -0.25f * ReferenceWallHeight / BlockSize;

        public float GetLevitatingY() => -0.75f * ReferenceWallHeight / BlockSize;

        public float GetLevitatingStepSize() => ReferenceWallHeight / (BlockSize * 40.0f);

        public void SetCameraHeight(Race race)
        {
            // Race-dependent additional height
            // in relation to a full wall height.
            float add = race switch
            {
                Race.Human => -0.04f,
                Race.Elf => 0.0f,
                Race.Dwarf => -0.125f,
                Race.Gnome => -0.125f,
                Race.HalfElf => 0.01f,
                Race.Sylphe => -0.09f,
                Race.Felinic => 0.0f,
                Race.Thalionic => -0.04f,
                _ => 0.0f
            };

            camera.GroundY = (-0.5f - add) * ReferenceWallHeight / BlockSize;
            camera.UpdatePosition();
        }

        public void Destroy(bool reset = false)
        {
            floorColor?.Delete();
            ceilingColor?.Delete();
            floor?.Delete();
            ceiling?.Delete();
            horizonSprite?.Destroy();
            skyColors?.ForEach(c => c?.Delete());
            if (reset)
            {
                stars?.ForEach(s => s.Value?.Delete());
                stars.Clear();
            }

            floorColor = null;
            ceilingColor = null;
            floor = null;
            ceiling = null;
            horizonSprite = null;
            skyColors = null;

            walls.Values.ToList().ForEach(walls => walls.ForEach(wall => wall?.Delete()));
            objects.Values.ToList().ForEach(objects => objects.ForEach(obj => obj?.Destroy()));
            mapCharacters.Values.ToList().ForEach(mc => mc?.Destroy());

            walls.Clear();
            objects.Clear();
            mapCharacters.Clear();

            blockCollisionBodies.Clear();
            playerBlockingBlocks.Clear();
            characterBlockingBlocks.Clear();
            monsterBlockSightBlocks.Clear();
        }

        void EnsureLabBackgroundGraphics(IGraphicProvider graphicProvider)
        {
            if (labBackgroundGraphics == null)
                labBackgroundGraphics = graphicProvider.GetGraphics(GraphicType.LabBackground).ToArray();
        }

        void EnsureChangeableBlocks()
        {
            if (!labdataChangeableBlocks.ContainsKey(Map.TilesetOrLabdataIndex))
            {
               var blockIndices = new List<uint>();

                foreach (var mapEvent in Map.Events)
                {
                    if (mapEvent.Type == EventType.ChangeTile)
                    {
                        if (!(mapEvent is ChangeTileEvent changeTileEvent))
                            throw new AmbermoonException(ExceptionScope.Data, "Invalid map event.");

                        uint index = Map.PositionToTileIndex(changeTileEvent.X - 1, changeTileEvent.Y - 1);

                        if (!blockIndices.Contains(index))
                            blockIndices.Add(index);
                    }
                }

                labdataChangeableBlocks.Add(Map.TilesetOrLabdataIndex, blockIndices);
            }
        }

        void EnsureLabdataTextureAtlas()
        {
            if (!labdataTextures.ContainsKey(Map.TilesetOrLabdataIndex))
            {
                var graphics = new Dictionary<uint, Graphic>();

                foreach (var obj in labdata.Objects)
                {
                    foreach (var subObj in obj.SubObjects)
                    {
                        if (!graphics.ContainsKey(subObj.Object.TextureIndex))
                            graphics.Add(subObj.Object.TextureIndex, labdata.ObjectGraphics[labdata.ObjectInfos.IndexOf(subObj.Object)]);
                    }
                }
                for (int i = 0; i < labdata.WallGraphics.Count; ++i)
                    graphics.Add((uint)i + 1000u, labdata.WallGraphics[i]);
                if (labdata.FloorGraphic != null)
                    graphics.Add(10000u, labdata.FloorGraphic);
                if (labdata.CeilingGraphic != null)
                    graphics.Add(10001u, labdata.CeilingGraphic);

                graphics.Add(10002u, labBackgroundGraphics[(int)Map.World]);

                labdataTextures.Add(Map.TilesetOrLabdataIndex, TextureAtlasManager.Instance.CreateFromGraphics(graphics, 1));
            }

            textureAtlas = labdataTextures[Map.TilesetOrLabdataIndex];
            renderView.GetLayer(Layer.Map3DBackground).Texture = textureAtlas.Texture;
            renderView.GetLayer(Layer.Map3D).Texture = textureAtlas.Texture;
            renderView.GetLayer(Layer.Billboards3D).Texture = textureAtlas.Texture;
        }

        Position GetObjectTextureOffset(uint objectIndex)
        {
            return textureAtlas.GetOffset(objectIndex);
        }

        Position GetWallTextureOffset(uint wallIndex)
        {
            return textureAtlas.GetOffset(wallIndex + 1000u);
        }

        Position FloorTextureOffset => textureAtlas.GetOffset(10000u);
        Position CeilingTextureOffset => textureAtlas.GetOffset(10001u);
        Position HorizonTextureOffset => textureAtlas.GetOffset(10002u);

        void AddCharacters()
        {
            for (uint characterIndex = 0; characterIndex < Map.CharacterReferences.Length; ++characterIndex)
            {
                var characterReference = Map.CharacterReferences[characterIndex];

                if (characterReference == null)
                    break;

                AddMapCharacter(renderView.Surface3DFactory, renderView.GetLayer(Layer.Billboards3D), characterIndex, characterReference);
            }
        }

        public void Pause()
        {
            foreach (var character in mapCharacters)
                character.Value.Pause();
        }

        public void Resume()
        {
            foreach (var character in mapCharacters)
                character.Value.Resume();
        }

        public AutomapType AutomapTypeFromBlock(uint x, uint y)
        {
            var characterEventId = mapCharacters.Select(c => c.Value).FirstOrDefault(c => c.Active && c.Position.X == x && c.Position.Y == y)?.EventId ?? 0;

            if (characterEventId != 0)
            {
                var type = Map.EventAutomapTypes[(int)characterEventId - 1];

                if (type != AutomapType.None)
                    return type;
            }

            var block = Map.Blocks[x, y];

            if (block.MapEventId != 0 && game.CurrentSavegame.IsEventActive(Map.Index, block.MapEventId - 1))
            {
                var type = Map.EventAutomapTypes[(int)block.MapEventId - 1];

                if (type != AutomapType.None)
                    return type;
            }

            if (block.WallIndex != 0)
                return labdata.Walls[((int)block.WallIndex - 1) % labdata.Walls.Count].AutomapType;
            else if (block.ObjectIndex != 0)
                return labdata.Objects[((int)block.ObjectIndex - 1) % labdata.Objects.Count].AutomapType;

            return AutomapType.None;
        }

        void GetObjectPosition(Labdata.ObjectPosition objectPosition, float baseX, float baseY, out float x, out float y, out float z,
            bool floorObject, out Size size)
        {
            size = new Size((int)objectPosition.Object.MappedTextureWidth, (int)objectPosition.Object.MappedTextureHeight);
            baseY = -Map.Height * Global.DistancePerBlock + baseY;
            x = baseX + objectPosition.X * Global.DistancePerBlock / BlockSize;
            z = baseY + Global.DistancePerBlock - Global.DistancePerBlock * objectPosition.Y / BlockSize;

            if (floorObject)
            {
                y = Util.Limit(1, objectPosition.Z, ReferenceWallHeight - 1) * labdata.WallHeight * Global.DistancePerBlock / (ReferenceWallHeight * BlockSize);
            }
            else
            {
                // TODO: This works quiet well but not in all cases. For example the hanging stuff in grandfather's cellar are
                //       a bit too small. Is scaled by 1/3 but should be 2/3 when comparing with original.
                y = objectPosition.Z + objectPosition.Object.MappedTextureHeight;
                if (y > 341)
                {
                    size.Height = 341 - objectPosition.Z;
                    float factor = (float)size.Height / objectPosition.Object.MappedTextureHeight;
                    size.Width = Util.Round(factor * objectPosition.Object.MappedTextureWidth);
                    y = 341;
                }
                y *= labdata.WallHeight * Global.DistancePerBlock / (ReferenceWallHeight * BlockSize);
                //(objectPosition.Z + objectPosition.Object.MappedTextureHeight) * labdata.WallHeight * Global.DistancePerBlock / (ReferenceWallHeight * BlockSize);
            }
        }

        void UpdateCharacterSurfaceCoordinates(FloatPosition position, ISurface3D surface, Labdata.ObjectPosition objectPosition)
        {
            GetObjectPosition(objectPosition, position.X, position.Y, out float x, out float y, out float z,
                surface.Type == SurfaceType.BillboardFloor, out _);
            surface.X = x;
            surface.Y = y;
            surface.Z = z;
        }

        void UpdateCharacterSurfaceCoordinates(Position position, ISurface3D surface, Labdata.ObjectPosition objectPosition,
            float xOffset = 0.0f, float yOffset = 0.0f)
        {
            GetObjectPosition(objectPosition, position.X * Global.DistancePerBlock, position.Y * Global.DistancePerBlock,
                out float x, out float y, out float z, surface.Type == SurfaceType.BillboardFloor, out _);
            surface.X = x + xOffset;
            surface.Y = y;
            surface.Z = z + yOffset;
        }

        MapCharacter CreateMapCharacter(ISurface3DFactory surfaceFactory, IRenderLayer layer, uint characterIndex,
            Labdata.ObjectPosition objectPosition, Map.CharacterReference characterReference, MapCharacter parent,
            float extrude)
        {
            float wallHeight = WallHeight;
            var objectInfo = objectPosition.Object;
            bool floorObject = objectInfo.Flags.HasFlag(Tileset.TileFlags.Floor);
            var mapObject = floorObject
                ? surfaceFactory.Create(SurfaceType.BillboardFloor,
                    Global.DistancePerBlock * objectInfo.MappedTextureWidth / BlockSize,
                    Global.DistancePerBlock * objectInfo.MappedTextureHeight / BlockSize,
                    objectInfo.TextureWidth, objectInfo.TextureHeight, objectInfo.TextureWidth,
                    objectInfo.TextureHeight, true, Math.Max(1, (int)objectInfo.NumAnimationFrames),
                    0.7075f * Global.DistancePerBlock) // This ensures drawing over the surrounding floor. It is a bit higher than half the diagonal -> sqrt(2) / 2.
                : surfaceFactory.Create(SurfaceType.Billboard,
                    Global.DistancePerBlock * objectInfo.MappedTextureWidth / BlockSize,
                    wallHeight * objectInfo.MappedTextureHeight / ReferenceWallHeight,
                    objectInfo.TextureWidth, objectInfo.TextureHeight, objectInfo.TextureWidth,
                    objectInfo.TextureHeight, true, Math.Max(1, (int)objectInfo.NumAnimationFrames),
                    extrude);
            mapObject.Layer = layer;
            mapObject.PaletteIndex = (byte)(Map.PaletteIndex - 1);
            var initialPosition = new Position(characterReference.Positions[0]);
            initialPosition.Offset(-1, -1);
            UpdateCharacterSurfaceCoordinates(initialPosition, mapObject, objectPosition);
            mapObject.TextureAtlasOffset = GetObjectTextureOffset(objectInfo.TextureIndex);
            var mapCharacter = new MapCharacter(game, this, mapObject, characterIndex, characterReference,
                objectPosition, objectInfo.TextureIndex, parent, objectInfo.Flags.HasFlag(Tileset.TileFlags.AlternateAnimation),
                objectInfo.NumAnimationFrames, 8.0f);
            mapCharacter.Active = !game.CurrentSavegame.GetCharacterBit(Map.Index, characterIndex);
            if (mapCharacter.Active)
                mapObject.Visible = true;
            return mapCharacter;
        }

        void AddMapCharacter(ISurface3DFactory surfaceFactory, IRenderLayer layer, uint characterIndex,
            Map.CharacterReference characterReference)
        {
            float extrude = 8.0f * ExtrudeStep;
            var obj = labdata.Objects[(int)characterReference.GraphicIndex - 1];
            var subObject = obj.SubObjects[0];
            var mapCharacter = CreateMapCharacter(surfaceFactory, layer, characterIndex, subObject, characterReference, null, extrude);
            for (int i = 1; i < obj.SubObjects.Count; ++i)
            {
                extrude -= ExtrudeStep;
                mapCharacter.AddChild(CreateMapCharacter(surfaceFactory, layer, characterIndex, obj.SubObjects[i], characterReference, mapCharacter, extrude));
            }
            mapCharacters.Add(characterIndex, mapCharacter);
        }

        void AddObject(ISurface3DFactory surfaceFactory, IRenderLayer layer, uint mapX, uint mapY, Labdata.Object obj)
        {
            uint blockIndex = mapX + mapY * (uint)Map.Width;
            blockCollisionBodies.Add(blockIndex, new List<ICollisionBody>(8));

            float wallHeight = WallHeight;
            float extrude = 8.0f * ExtrudeStep;

            foreach (var subObject in obj.SubObjects)
            {
                var objectInfo = subObject.Object;
                bool floorObject = objectInfo.Flags.HasFlag(Tileset.TileFlags.Floor);
                GetObjectPosition(subObject, mapX * Global.DistancePerBlock, mapY * Global.DistancePerBlock,
                    out float x, out float y, out float z, floorObject, out Size size);
                var mapObject = floorObject
                    ? surfaceFactory.Create(SurfaceType.BillboardFloor,
                        Global.DistancePerBlock * size.Width / BlockSize,
                        Global.DistancePerBlock * size.Height / BlockSize,
                        objectInfo.TextureWidth, objectInfo.TextureHeight, objectInfo.TextureWidth,
                        objectInfo.TextureHeight, true, Math.Max(1, (int)objectInfo.NumAnimationFrames),
                        extrude)
                    : surfaceFactory.Create(SurfaceType.Billboard,
                        Global.DistancePerBlock * size.Width / BlockSize,
                        wallHeight * size.Height / ReferenceWallHeight,
                        objectInfo.TextureWidth, objectInfo.TextureHeight, objectInfo.TextureWidth,
                        objectInfo.TextureHeight, true, Math.Max(1, (int)objectInfo.NumAnimationFrames),
                        extrude);
                extrude -= ExtrudeStep;
                mapObject.Layer = layer;
                mapObject.PaletteIndex = (byte)(Map.PaletteIndex - 1);
                mapObject.X = x;
                mapObject.Y = y;
                mapObject.Z = z;
                mapObject.TextureAtlasOffset = GetObjectTextureOffset(objectInfo.TextureIndex);
                mapObject.Visible = true; // TODO: not all objects should be always visible
                objects.SafeAdd(blockIndex, new MapObject(this, mapObject, objectInfo.TextureIndex,
                    objectInfo.Flags.HasFlag(Tileset.TileFlags.AlternateAnimation), objectInfo.NumAnimationFrames, 8.0f));

                bool blockPlayer = objectInfo.Flags.HasFlag(Tileset.TileFlags.BlockAllMovement) || !objectInfo.Flags.HasFlag(Tileset.TileFlags.AllowMovementWalk);
                bool blockMonster = objectInfo.Flags.HasFlag(Tileset.TileFlags.BlockAllMovement) || !objectInfo.Flags.HasFlag(Tileset.TileFlags.AllowMovementMonster);

                // Small objects should not block
                if (objectInfo.MappedTextureWidth < BlockSize / 5)
                {
                    blockPlayer = false;
                    blockMonster = false;
                }

                if (blockPlayer || blockMonster)
                {
                    blockCollisionBodies[blockIndex].Add(new CollisionSphere3D
                    {
                        CenterX = mapObject.X,
                        CenterZ = -mapObject.Z,
                        Radius = 0.25f * Global.DistancePerBlock * objectInfo.MappedTextureWidth / BlockSize,
                        PlayerCanPass = !blockPlayer
                    });
                }

                if (blockPlayer && !playerBlockingBlocks.Contains(blockIndex))
                    playerBlockingBlocks.Add(blockIndex);
                if (blockMonster && !characterBlockingBlocks.Contains(blockIndex))
                    characterBlockingBlocks.Add(blockIndex);
            }
        }

        void AddWall(ISurface3DFactory surfaceFactory, IRenderLayer layer, uint mapX, uint mapY, uint wallIndex)
        {
            wallIndex %= (uint)labdata.Walls.Count;

            uint blockIndex = mapX + mapY * (uint)Map.Width;
            blockCollisionBodies.Add(blockIndex, new List<ICollisionBody>(4));
            float wallHeight = WallHeight;
            var wallTextureOffset = GetWallTextureOffset(wallIndex);
            var wallFlags = labdata.Walls[(int)wallIndex].Flags;
            bool alpha = wallFlags.HasFlag(Tileset.TileFlags.Transparency);
            bool blockPlayer = wallFlags.HasFlag(Tileset.TileFlags.BlockAllMovement) || !wallFlags.HasFlag(Tileset.TileFlags.AllowMovementWalk);
            bool blockMonster = wallFlags.HasFlag(Tileset.TileFlags.BlockAllMovement) || !wallFlags.HasFlag(Tileset.TileFlags.AllowMovementMonster);

            if (blockPlayer && !playerBlockingBlocks.Contains(blockIndex))
                playerBlockingBlocks.Add(blockIndex);
            if (blockMonster && !characterBlockingBlocks.Contains(blockIndex))
                characterBlockingBlocks.Add(blockIndex);

            if (!monsterBlockSightBlocks.Contains(blockIndex) && wallFlags.HasFlag(Tileset.TileFlags.BlockSight))
                monsterBlockSightBlocks.Add(blockIndex);

            // This is used to determine if surrounded tiles should add a wall.
            // Free block means no wall, non-blocking wall or a transparent/removable wall.
            bool IsFreeBlock(uint mapX, uint mapY)
            {
                var block = Map.Blocks[mapX, mapY];

                if (block.MapBorder)
                    return false;

                if (block.WallIndex == 0)
                    return true;

                var wall = labdata.Walls[((int)block.WallIndex - 1) % labdata.Walls.Count];

                return wall.AutomapType != AutomapType.Wall ||
                    wall.Flags.HasFlag(Tileset.TileFlags.Transparency) ||
                    !(wall.Flags.HasFlag(Tileset.TileFlags.BlockAllMovement) || !wall.Flags.HasFlag(Tileset.TileFlags.AllowMovementWalk)) ||
                    labdataChangeableBlocks[Map.TilesetOrLabdataIndex].Contains(Map.PositionToTileIndex(mapX, mapY));
            }

            void AddSurface(WallOrientation wallOrientation, float x, float z)
            {
                var wall = surfaceFactory.Create(SurfaceType.Wall, Global.DistancePerBlock, wallHeight,
                    TextureWidth, TextureHeight, TextureWidth, TextureHeight, alpha, 1, 0.0f, wallOrientation,
                    Map.Flags.HasFlag(MapFlags.Outdoor) ? labdata.CeilingColorIndex : (byte)0);
                wall.Layer = layer;
                wall.PaletteIndex = (byte)(Map.PaletteIndex - 1);
                wall.X = x;
                wall.Y = wallHeight;
                wall.Z = z;
                wall.TextureAtlasOffset = wallTextureOffset;
                wall.Visible = true; // TODO: not all walls should be always visible
                walls.SafeAdd(blockIndex, wall);

                blockCollisionBodies[blockIndex].Add(new CollisionLine3D
                {
                    X = wallOrientation == WallOrientation.Rotated180 ? x - Global.DistancePerBlock : x,
                    Z = -(wallOrientation == WallOrientation.Rotated270 ? z - Global.DistancePerBlock : z),
                    Horizontal = wallOrientation == WallOrientation.Normal || wallOrientation == WallOrientation.Rotated180,
                    Length = Global.DistancePerBlock,
                    PlayerCanPass = !blockPlayer
                });
            }

            float baseX = mapX * Global.DistancePerBlock;
            float baseY = (-Map.Height + mapY) * Global.DistancePerBlock;

            // front face
            if (mapY > 0 && IsFreeBlock(mapX, mapY - 1))
                AddSurface(WallOrientation.Normal, baseX, baseY);

            // left face
            if (mapX < Map.Width - 1 && IsFreeBlock(mapX + 1, mapY))
                AddSurface(WallOrientation.Rotated90, baseX + Global.DistancePerBlock, baseY);

            // back face
            if (mapY < Map.Height - 1 && IsFreeBlock(mapX, mapY + 1))
                AddSurface(WallOrientation.Rotated180, baseX + Global.DistancePerBlock, baseY + Global.DistancePerBlock);

            // right face
            if (mapX > 0 && IsFreeBlock(mapX - 1, mapY))
                AddSurface(WallOrientation.Rotated270, baseX, baseY + Global.DistancePerBlock);
        }

        internal void UpdateBlock(uint x, uint y)
        {
            uint index = x + y * (uint)Map.Width;
            bool wallRemoved = false;

            if (walls.ContainsKey(index))
            {
                walls[index].ForEach(wall => wall?.Delete());
                walls.Remove(index);
                wallRemoved = true;
            }

            if (objects.ContainsKey(index))
            {
                objects[index].ForEach(obj => obj?.Destroy());
                objects.Remove(index);
            }

            if (blockCollisionBodies.ContainsKey(index))
                blockCollisionBodies.Remove(index);
            if (playerBlockingBlocks.Contains(index))
                playerBlockingBlocks.Remove(index);
            if (characterBlockingBlocks.Contains(index))
                characterBlockingBlocks.Remove(index);
            if (monsterBlockSightBlocks.Contains(index))
                monsterBlockSightBlocks.Remove(index);

            var surfaceFactory = renderView.Surface3DFactory;
            var layer = renderView.GetLayer(Layer.Map3D);
            var billboardLayer = renderView.GetLayer(Layer.Billboards3D);
            var block = Map.Blocks[x, y];

            if (block.WallIndex != 0)
                AddWall(surfaceFactory, layer, x, y, block.WallIndex - 1);
            else if (block.ObjectIndex != 0)
                AddObject(surfaceFactory, billboardLayer, x, y, labdata.Objects[((int)block.ObjectIndex - 1) % labdata.Objects.Count]);

            if (wallRemoved && block.WallIndex == 0)
            {
                // Totally removed a wall -> check if adjacent walls need some surfaces.
                for (int testY = -1; testY <= 1; ++testY)
                {
                    int blockY = (int)y + testY;

                    if (blockY < 0 || blockY >= Map.Height)
                        continue;

                    for (int testX = -1; testX <= 1; ++testX)
                    {
                        int blockX = (int)x + testX;

                        if (blockX < 0 || blockX >= Map.Width)
                            continue;

                        var adjacentBlock = Map.Blocks[(uint)blockX, (uint)blockY];

                        if (adjacentBlock.WallIndex != 0)
                        {
                            // Recreate the adjacent wall
                            uint adjacentIndex = (uint)(blockX + blockY * Map.Width);
                            if (walls.ContainsKey(adjacentIndex))
                                walls[adjacentIndex]?.ForEach(wall => wall?.Delete());
                            walls.Remove(adjacentIndex);
                            if (blockCollisionBodies.ContainsKey(adjacentIndex))
                                blockCollisionBodies.Remove(adjacentIndex);
                            if (playerBlockingBlocks.Contains(adjacentIndex))
                                playerBlockingBlocks.Remove(adjacentIndex);
                            if (characterBlockingBlocks.Contains(adjacentIndex))
                                characterBlockingBlocks.Remove(adjacentIndex);
                            if (monsterBlockSightBlocks.Contains(adjacentIndex))
                                monsterBlockSightBlocks.Remove(adjacentIndex);
                            AddWall(surfaceFactory, layer, (uint)blockX, (uint)blockY, adjacentBlock.WallIndex - 1);
                        }
                    }
                }
            }
        }

        void UpdateSurfaces()
        {
            // Delete all surfaces
            Destroy();

            var surfaceFactory = renderView.Surface3DFactory;
            var layer = renderView.GetLayer(Layer.Map3D);
            var billboardLayer = renderView.GetLayer(Layer.Billboards3D);

            // Add floor and ceiling
            if (labdata.FloorGraphic != null)
            {
                floor = surfaceFactory.Create(SurfaceType.Floor,
                    (Map.Width + 16) * Global.DistancePerBlock, (Map.Height + 16) * Global.DistancePerBlock,
                    FloorTextureWidth, FloorTextureHeight,
                    (uint)(Map.Width + 16) * FloorTextureWidth, (uint)(Map.Height + 16) * FloorTextureHeight, false);
                floor.PaletteIndex = (byte)(Map.PaletteIndex - 1);
                floor.Layer = layer;
                floor.X = -8 * Global.DistancePerBlock;
                floor.Y = 0.0f;
                floor.Z = -(Map.Height + 8) * Global.DistancePerBlock;
                floor.TextureAtlasOffset = FloorTextureOffset;
                floor.Visible = true;
            }
            if (labdata.CeilingGraphic != null)
            {
                ceiling = surfaceFactory.Create(SurfaceType.Ceiling,
                    (Map.Width + 16) * Global.DistancePerBlock, (Map.Height + 16) * Global.DistancePerBlock,
                    FloorTextureWidth, FloorTextureHeight,
                    (uint)(Map.Width + 16) * FloorTextureWidth, (uint)(Map.Height + 16) * FloorTextureHeight, false);
                ceiling.PaletteIndex = (byte)(Map.PaletteIndex - 1);
                ceiling.Layer = layer;
                ceiling.X = -8 * Global.DistancePerBlock;
                ceiling.Y = WallHeight;
                ceiling.Z = 8 * Global.DistancePerBlock;
                ceiling.TextureAtlasOffset = CeilingTextureOffset;
                ceiling.Visible = true;
            }

            // Add walls and objects
            for (uint y = 0; y < Map.Height; ++y)
            {
                for (uint x = 0; x < Map.Width; ++x)
                {
                    var block = Map.Blocks[x, y];

                    if (block.WallIndex != 0)
                        AddWall(surfaceFactory, layer, x, y, block.WallIndex - 1);
                    else if (block.ObjectIndex != 0)
                        AddObject(surfaceFactory, billboardLayer, x, y, labdata.Objects[((int)block.ObjectIndex - 1) % labdata.Objects.Count]);
                }
            }
        }

        public void HideSky()
        {
            renderView.PaletteReplacement = null;
            renderView.SetSkyColorReplacement(null, null);
            stars.ForEach(s => s.Value.Visible = false);
            if (skyColors != null)
                skyColors.ForEach(c => c?.Delete());
            SetColors(null);            
        }

        public void UpdateSky(ILightEffectProvider lightEffectProvider, ITime time)
        {
            if (Map?.Flags.HasFlag(MapFlags.Outdoor) != true)
                return;

            var skyParts = lightEffectProvider.GetSkyParts(Map, time.Hour, time.Minute,
                renderView.GraphicProvider, out var paletteReplacement);

            renderView.PaletteReplacement = paletteReplacement;

            SetColors(paletteReplacement);

            if (skyParts == null)
            {
                renderView.SetSkyColorReplacement(null, null);
                return;
            }

            if (skyColors != null)
                skyColors.ForEach(c => c?.Delete());

            bool canSee = game.CanSee();

            if (canSee)
            {
                skyColors = skyParts.Select(part =>
                {
                    var skyColor = renderView.ColoredRectFactory.Create(Global.Map3DViewWidth, part.Height, new Color(part.Color), 1);
                    skyColor.X = Global.Map3DViewX;
                    skyColor.Y = Global.Map3DViewY + part.Y;
                    skyColor.Layer = ceilingColor.Layer;
                    skyColor.Visible = true;
                    return skyColor;
                }).ToList();
                UpdateStars(Util.Round(8.0f * -144.0f * camera.Angle / 360.0f));
                renderView.SetSkyColorReplacement(labdata.CeilingColorIndex, skyColors.Last().Color);
            }
            stars.ForEach(s => s.Value.Visible = canSee && (time.Hour >= 19 || time.Hour < 7));            
        }

        void UpdateStars(int scrollX)
        {
            if (Map == null)
                return;

            const int starAreaWidth = 8 * Global.Map3DViewWidth;
            bool showStars = game.GameTime.Hour >= 19 || game.GameTime.Hour < 7;
            var starColor = !showStars ? null
                : game.GameTime.Hour < 5 || game.GameTime.Hour >= 21 ? game.GetPaletteColor((int)Map.PaletteIndex, 31)
                : game.GameTime.Hour == 5 || game.GameTime.Hour == 20 ? game.GetPaletteColor((int)Map.PaletteIndex, 30)
                : game.GameTime.Hour == 6 || game.GameTime.Hour == 19 ? game.GetPaletteColor((int)Map.PaletteIndex, 29)
                : null;

            stars.ForEach(s =>
            {
                s.Value.X = s.Key.X + scrollX;

                if (s.Value.X < Global.Map3DViewX - (starAreaWidth - Global.Map3DViewWidth))
                    s.Value.X += starAreaWidth;
                else if (s.Value.X >= Global.Map3DViewX + (starAreaWidth - Global.Map3DViewWidth))
                    s.Value.X -= starAreaWidth;

                if (starColor != null)
                    s.Value.Color = starColor;
            });
        }

        public void Update(uint ticks, ITime gameTime)
        {
            foreach (var mapObject in objects)
                mapObject.Value.ForEach(obj => obj.Update(ticks));

            foreach (var mapCharacter in mapCharacters.Values)
                mapCharacter.Update(ticks, gameTime);
        }

        public void UpdateCharacterVisibility(uint characterIndex)
        {
            if (Map.CharacterReferences[characterIndex] == null)
                throw new AmbermoonException(ExceptionScope.Application, "Null map character");

            mapCharacters[characterIndex].Active = !game.CurrentSavegame.GetCharacterBit(Map.Index, characterIndex);
        }

        public CollisionDetectionInfo3D GetCollisionDetectionInfoForPlayer(Position position)
        {
            var info = new CollisionDetectionInfo3D();

            for (int y = Math.Max(0, position.Y - 1); y <= Math.Min(Map.Height - 1, position.Y + 1); ++y)
            {
                for (int x = Math.Max(0, position.X - 1); x <= Math.Min(Map.Width - 1, position.X + 1); ++x)
                {
                    uint blockIndex = (uint)(x + y * Map.Width);

                    if (playerBlockingBlocks.Contains(blockIndex) && blockCollisionBodies.ContainsKey(blockIndex))
                    {
                        foreach (var collisionBody in blockCollisionBodies[blockIndex])
                            info.CollisionBodies.Add(collisionBody);
                    }
                }
            }

            return info;
        }

        public bool EventBlocksCharacter(Position position)
        {
            var eventId = Map.Blocks[position.X, position.Y].MapEventId;

            if (eventId != 0 && game.CurrentSavegame.IsEventActive(Map.Index, eventId))
            {
                var @event = Map.EventList[(int)eventId - 1];

                switch (@event.Type)
                {
                    case EventType.Door:
                    case EventType.EnterPlace:
                    case EventType.Riddlemouth:
                    case EventType.Teleport:
                        return true;
                }
            }

            return false;
        }

        public CollisionDetectionInfo3D GetCollisionDetectionInfoForMonsterFromPositions(params Position[] positions)
        {
            var info = new CollisionDetectionInfo3D();

            foreach (var position in positions)
            {
                uint blockIndex = (uint)(position.X + position.Y * Map.Width);

                if (characterBlockingBlocks.Contains(blockIndex) && blockCollisionBodies.ContainsKey(blockIndex))
                {
                    foreach (var collisionBody in blockCollisionBodies[blockIndex])
                        info.CollisionBodies.Add(collisionBody);

                    if (EventBlocksCharacter(position))
                    {
                        float x = position.X * Global.DistancePerBlock + 0.5f * BlockSize;
                        float z = position.Y * Global.DistancePerBlock + Global.DistancePerBlock - 0.5f * BlockSize;
                        info.CollisionBodies.Add(new CollisionSphere3D
                        {
                            CenterX = x,
                            CenterZ = -z,
                            Radius = 0.5f * BlockSize,
                            PlayerCanPass = false
                        });
                        break;
                    }
                }
                // TODO: characters on tiles
                /*else
                {
                    foreach (var mapCharacter in mapCharacters.Where(c => c.Value?.Active == true && c.Value.Position == position))
                    {
                        var flags = mapCharacter.Value.TileFlags;

                        if (!flags.HasFlag(Tileset.TileFlags.UseBackgroundTileFlags))
                        {
                            var tile = new Tileset.Tile { Flags = flags };

                            if (tile.Flags.HasFlag(Tileset.TileFlags.BlockAllMovement))
                                info.CollisionBodies.Add(mapCharacter.Value.GetCollisionBody());
                        }
                    }
                }*/
            }

            return info;
        }

        public bool TriggerEvents(Game game, EventTrigger trigger,
            uint x, uint y, uint ticks, Savegame savegame)
        {
            // first check for NPC interaction
            if (trigger == EventTrigger.Eye || trigger == EventTrigger.Mouth ||
                trigger == EventTrigger.Hand || trigger >= EventTrigger.Item0)
            {
                foreach (var mapCharacter in mapCharacters)
                {
                    if (mapCharacter.Value.Position.X == x && mapCharacter.Value.Position.Y == y)
                    {
                        if (mapCharacter.Value.Interact(trigger, false))
                            return true;
                    }
                }
            }

            return Map.TriggerEvents(game, trigger, x, y, ticks, savegame, out _);
        }
    }
}

﻿using Ambermoon.Data.Enumerations;
using Ambermoon.Data.Legacy.Serialization;
using Ambermoon.Data.Serialization;
using System;

namespace Ambermoon.Data.Legacy.Characters
{
    public abstract class CharacterReader
    {
        internal void ReadCharacter(Character character, IDataReader dataReader)
        {
            dataReader.Position = 0;

            if (dataReader.ReadByte() != (byte)character.Type)
                throw new Exception("Wrong character type.");

            character.Gender = (Gender)dataReader.ReadByte();
            character.Race = (Race)dataReader.ReadByte();
            character.Class = (Class)dataReader.ReadByte();
            character.SpellMastery = (SpellTypeMastery)dataReader.ReadByte();
            character.Level = dataReader.ReadByte();
            character.NumberOfFreeHands = dataReader.ReadByte();
            character.NumberOfFreeFingers = dataReader.ReadByte();
            character.SpokenLanguages = (Language)dataReader.ReadByte();
            character.PortraitIndex = dataReader.ReadWord();
            ProcessIfMonster(dataReader, character, (Monster monster, ushort value) => monster.CombatGraphicIndex = (MonsterGraphicIndex)value);
            character.UnknownBytes13 = dataReader.ReadBytes(3); // Unknown
            character.SpellTypeImmunity = (SpellTypeImmunity)dataReader.ReadByte();
            character.AttacksPerRound = dataReader.ReadByte();
            ProcessIfMonster(dataReader, character, (Monster monster, byte value) => monster.MonsterFlags = (MonsterFlags)value);
            character.Element = (CharacterElement)dataReader.ReadByte();
            character.SpellLearningPoints = dataReader.ReadWord();
            character.TrainingPoints = dataReader.ReadWord();
            character.Gold = dataReader.ReadWord();
            character.Food = dataReader.ReadWord();
            character.UnknownWord28 = dataReader.ReadWord(); // Unknown
            character.Ailments = (Ailment)dataReader.ReadWord();
            ProcessIfMonster(dataReader, character, (Monster monster, ushort value) => monster.DefeatExperience = value);
            character.UnknownWord34 = dataReader.ReadWord(); // Unknown
            // mark of return location is stored here: word x, word y, word mapIndex
            ProcessIfPartyMember(dataReader, character, (PartyMember member, ushort value) => member.MarkOfReturnX = value);
            ProcessIfPartyMember(dataReader, character, (PartyMember member, ushort value) => member.MarkOfReturnY = value);
            ProcessIfPartyMember(dataReader, character, (PartyMember member, ushort value) => member.MarkOfReturnMapIndex = value);
            foreach (var attribute in character.Attributes) // Note: this includes Age and the 10th unused attribute
            {
                attribute.CurrentValue = dataReader.ReadWord();
                attribute.MaxValue = dataReader.ReadWord();
                attribute.BonusValue = dataReader.ReadWord();
                attribute.Unknown = dataReader.ReadWord();
            }
            foreach (var ability in character.Abilities)
            {
                ability.CurrentValue = dataReader.ReadWord();
                ability.MaxValue = dataReader.ReadWord();
                ability.BonusValue = dataReader.ReadWord();
                ability.Unknown = dataReader.ReadWord();
            }
            character.HitPoints.CurrentValue = dataReader.ReadWord();
            character.HitPoints.MaxValue = dataReader.ReadWord();
            character.HitPoints.BonusValue = dataReader.ReadWord();
            character.SpellPoints.CurrentValue = dataReader.ReadWord();
            character.SpellPoints.MaxValue = dataReader.ReadWord();
            character.SpellPoints.BonusValue = dataReader.ReadWord();
            character.CombatDefense = (short)dataReader.ReadWord();
            character.DisplayedDefense = (short)dataReader.ReadWord();
            character.CombatAttack = (short)dataReader.ReadWord();
            character.DisplayedAttack = (short)dataReader.ReadWord();
            character.MagicAttack = (short)dataReader.ReadWord();
            character.MagicDefense = (short)dataReader.ReadWord();
            character.AttacksPerRoundPerLevel = dataReader.ReadWord();
            character.HitPointsPerLevel = dataReader.ReadWord();
            character.SpellPointsPerLevel = dataReader.ReadWord();
            character.SpellLearningPointsPerLevel = dataReader.ReadWord();
            character.TrainingPointsPerLevel = dataReader.ReadWord();
            character.UnknownWord236 = dataReader.ReadWord(); // Unknown
            character.ExperiencePoints = dataReader.ReadDword();
            character.LearnedHealingSpells = dataReader.ReadDword();
            character.LearnedAlchemisticSpells = dataReader.ReadDword();
            character.LearnedMysticSpells = dataReader.ReadDword();
            character.LearnedDestructionSpells = dataReader.ReadDword();
            character.LearnedSpellsType5 = dataReader.ReadDword();
            character.LearnedSpellsType6 = dataReader.ReadDword();
            character.LearnedSpellsType7 = dataReader.ReadDword();
            character.TotalWeight = dataReader.ReadDword();
            character.Name = dataReader.ReadString(16);

            int terminatingNullIndex = character.Name.IndexOf('\0');

            if (terminatingNullIndex != 0)
                character.Name = character.Name.Substring(0, terminatingNullIndex).TrimEnd();
            else
                character.Name = character.Name.TrimEnd();

            if (character.Type != CharacterType.NPC)
            {
                // Equipment
                foreach (var equipmentSlot in Enum.GetValues<EquipmentSlot>())
                {
                    if (equipmentSlot != EquipmentSlot.None)
                        ItemSlotReader.ReadItemSlot(character.Equipment.Slots[equipmentSlot], dataReader);
                }

                // Inventory
                for (int i = 0; i < Inventory.Width * Inventory.Height; ++i)
                    ItemSlotReader.ReadItemSlot(character.Inventory.Slots[i], dataReader);
            }
        }

        void ProcessIfMonster(IDataReader reader, Character character, Action<Monster, byte> processor)
        {
            if (character is Monster monster)
                processor(monster, reader.ReadByte());
            else
                reader.Position += 1;
        }

        void ProcessIfMonster(IDataReader reader, Character character, Action<Monster, ushort> processor)
        {
            if (character is Monster monster)
                processor(monster, reader.ReadWord());
            else
                reader.Position += 2;
        }

        void ProcessIfPartyMember(IDataReader reader, Character character, Action<PartyMember, ushort> processor)
        {
            if (character is PartyMember partyMember)
                processor(partyMember, reader.ReadWord());
            else
                reader.Position += 2;
        }
    }
}

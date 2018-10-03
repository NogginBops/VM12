using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VM12_Opcode;

namespace Profiler
{
    using VM12 = VM12.VM12;
    
    public partial class HotSpotView : UserControl
    {
        private VM12 vm12;

#if DEBUG
        private VM12.ProcMetadata metadata;
#endif

        private DataGridViewCellStyle JmpTargetStyle = new DataGridViewCellStyle();

        private DataGridViewRow JmpTargetRow;

        public HotSpotView()
        {
            InitializeComponent();

            dgvHotSpot.Columns["Address"].DefaultCellStyle.Format = "X";

            JmpTargetStyle.BackColor = Color.FromArgb(113, 232, 143);
        }

        internal void SetVM(VM12 vm12)
        {
            this.vm12 = vm12;
        }

#if DEBUG
        internal void SetProc(VM12.ProcMetadata metadata)
        {
            this.metadata = metadata;

            UpdateHotSpots(metadata);
        }
#endif

        internal void UpdateData()
        {
            UpdateCounts();
            // UpdateHotSpots(metadata);
        }

        private int InstructionLength(Opcode op)
        {
            switch (op)
            {
                case Opcode.Store_local:
                case Opcode.Store_local_l:
                case Opcode.Load_local:
                case Opcode.Load_local_l:
                case Opcode.Load_lit:
                case Opcode.Ret_v:
                case Opcode.Inc_local:
                case Opcode.Inc_local_l:
                case Opcode.Dec_local:
                case Opcode.Dec_local_l:
                    return 2;
                case Opcode.Load_lit_l:
                case Opcode.Call:
                    return 3;
                case Opcode.Jmp:
                    return 4;
                default:
                    return 1;
            }
        }

        Dictionary<JumpMode, string> JmpInstructionStrings = new Dictionary<JumpMode, string>(){
                { JumpMode.Jmp,     "Jmp"   },
                { JumpMode.Z,       "Jz"    },
                { JumpMode.Nz,      "Jnz"   },
                { JumpMode.C,       "Jc"    },
                { JumpMode.Cz,      "Jcz"   },
                { JumpMode.Gz,      "Jgz"   },
                { JumpMode.Lz,      "Jlz"   },
                { JumpMode.Ge,      "Jge"   },
                { JumpMode.Le,      "Jle"   },
                { JumpMode.Eq,      "Jeq"   },
                { JumpMode.Neq,     "Jneq"  },
                { JumpMode.Ro,      "Jro"   },
                { JumpMode.Z_l,     "Jzl"   },
                { JumpMode.Nz_l,    "Jnzl"  },
                { JumpMode.Gz_l,    "Jgzl"  },
                { JumpMode.Lz_l,    "Jlzl"  },
                { JumpMode.Ge_l,    "Jgel"  },
                { JumpMode.Le_l,    "Jlel"  },
                { JumpMode.Eq_l,    "Jeql"  },
                { JumpMode.Neq_l,   "Jneql" },
                { JumpMode.Ro_l,    "Ro_l"  },
        };

#if DEBUG
        private bool IsInterrupt(int location)
        {
            switch (location)
            {
                case 0xFFF_FF0:
                case 0xFFF_FE0:
                case 0xFFF_FD0:
                case 0xFFF_FC0:
                    return true;
                default:
                    return false;
            }
        }

        private void UpdateHotSpots(VM12.ProcMetadata metadata)
        {
            dgvHotSpot.Rows.Clear();
            if (metadata != null)
            {
                Opcode op = Opcode.Nop;
                bool isInterrupt = IsInterrupt(metadata.location);
                for (int i = isInterrupt ? 0 : 2; i < metadata.size; i += InstructionLength(op))
                {
                    int index = metadata.location + i;
                    op = (Opcode)vm12.MEM[index];
                    string opString = op.ToString();
                    switch (op)
                    {
                        case Opcode.Call:
                            opString = $":{vm12.GetMetadataFromOffset(vm12.MEM[index + 1] << 12 | vm12.MEM[index + 2]).name}";
                            break;
                        case Opcode.Load_lit:
                            opString = $"{op} #{vm12.MEM[index + 1]}";
                            break;
                        case Opcode.Load_lit_l:
                            opString = $"{op} #{vm12.MEM[index + 1] << 12 | vm12.MEM[index + 2]}";
                            break;
                        case Opcode.Jmp:
                            JumpMode jmpMode = (JumpMode)vm12.MEM[index + 1];
                            opString = $"{(JmpInstructionStrings.TryGetValue(jmpMode, out string jmpString) ? jmpString : "Jmp INVALID")} 0x{vm12.MEM[index + 2] << 12 | vm12.MEM[index + 3]:X}";
                            break;
                        case Opcode.Ret_v:
                            opString = $"Ret {vm12.MEM[index + 1]}";
                            break;
                    }

                    dgvHotSpot.Rows.Add(vm12.romInstructionCounter[index], opString, metadata.file, vm12.GetSourceCodeLineFromMetadataAndOffset(metadata, index), index);
                }
            }
        }
#endif

        private void UpdateCounts()
        {
#if DEBUG
            if (metadata != null)
            {
                int row = 0;
                Opcode op = Opcode.Nop;
                bool isInterrupt = IsInterrupt(metadata.location);
                for (int i = isInterrupt ? 0 : 2; i < metadata.size; i += InstructionLength(op))
                {
                    int index = metadata.location + i;
                    op = (Opcode)vm12.MEM[index];

                    dgvHotSpot.Rows[row++].Cells[0].Value = vm12.romInstructionCounter[index];
                }
            }
#endif
        }
        
        private void dvgHotSpot_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
#if DEBUG
            if (JmpTargetRow != null) JmpTargetRow.DefaultCellStyle = null;
            JmpTargetRow = null;

            if (dgvHotSpot.SelectedRows.Count == 1)
            {
                // Read the address of the selected index
                int address = (int) dgvHotSpot.Rows[e.RowIndex].Cells["Address"].Value;

                if ((Opcode) vm12.MEM[address] == Opcode.Jmp)
                {
                    // Use address to figure out the jump destination
                    int jmpTarget = vm12.MEM[address + 2] << 12 | vm12.MEM[address + 3];

                    if (jmpTarget >= metadata.location && jmpTarget < metadata.location + metadata.size)
                    {
                        foreach (DataGridViewRow row in dgvHotSpot.Rows)
                        {
                            DataGridViewCell addressCell = row.Cells["Address"];
                            if (((int)addressCell.Value) == jmpTarget)
                            {
                                JmpTargetRow = addressCell.OwningRow;
                                break;
                            }
                        }

                        if (JmpTargetRow != null)
                        {
                            JmpTargetRow.DefaultCellStyle = JmpTargetStyle;
                        }
                    }
                }

                Console.WriteLine(((DataGridView)sender).Rows[e.RowIndex].Cells[e.ColumnIndex]);
            }
#endif
        }
    }
}

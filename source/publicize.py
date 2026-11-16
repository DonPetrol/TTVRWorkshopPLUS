import dnfile,sys
src,dst=sys.argv[1],sys.argv[2]
pe=dnfile.dnPE(src); data=bytearray(open(src,'rb').read())
md=pe.net.mdtables
def off(row): 
    return pe.get_offset_from_rva(row.rva) if hasattr(row,'rva') else row.file_offset
import struct
n=0
for t,kind in ((md.TypeDef,'t'),(md.Field,'f'),(md.MethodDef,'m')):
    for i in range(t.num_rows):
        o=t.file_offset+i*t.row_size
        if kind=='t':
            v=struct.unpack_from('<I',data,o)[0]; vis=v&7
            new=(v&~7)|(1 if vis in(0,1) else 2)
            struct.pack_into('<I',data,o,new)
        elif kind=='f':
            v=struct.unpack_from('<H',data,o)[0]; struct.pack_into('<H',data,o,(v&~7)|6)
        else:
            o+=6  # RVA(4) ImplFlags(2) Flags(2)
            v=struct.unpack_from('<H',data,o)[0]; struct.pack_into('<H',data,o,(v&~7)|6)
        n+=1
open(dst,'wb').write(data); print(n)

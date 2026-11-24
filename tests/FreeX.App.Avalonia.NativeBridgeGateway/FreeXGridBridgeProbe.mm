#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include <dlfcn.h>
#define COM_GUIDS_MATERIALIZE
#include "avalonia-native.h"
#include "comimpl.h"
#import "automation.h"

static bool SameArray(NSArray* native, IAvnAutomationPeerArray* peers)
{
    if([native count] != peers->GetCount()) return false;
    for(uint i=0;i<peers->GetCount();i++)
    {
        ComPtr<IAvnAutomationPeer> peer;
        if(peers->Get(i,peer.getPPV())!=S_OK) return false;
        if(native[i] != [NSClassFromString(@"AvnAccessibilityElement") acquire:peer]) return false;
    }
    return true;
}

// No NSApplication/window is created. All owned inputs are consumed on every path.
extern "C" __attribute__((visibility("default"))) int FreeXGridBridgeProbe(
    IAvnAutomationPeer* ownedGrid, IAvnAutomationPeer* ownedCell, IAvnAutomationPeer* ownedChild,
    int rows,int columns,int row,int column,int focus,int press)
{
    ComPtr<IAvnAutomationPeer> grid(ownedGrid,true),cell(ownedCell,true),child(ownedChild,true);
    @autoreleasepool
    {
        ComPtr<IAvnGridProvider> provider;
        ComPtr<IAvnGridItemProvider> item;
        if(grid->QueryInterface(&IID_IAvnGridProvider,reinterpret_cast<void**>(provider.getPPV()))!=S_OK) return 10;
        if(cell->QueryInterface(&IID_IAvnGridItemProvider,reinterpret_cast<void**>(item.getPPV()))!=S_OK) return 11;
        if(!provider->Grid_IsSupported() || !item->GridItem_IsSupported()) return 12;
        if(provider->Grid_GetRowCount()!=rows || provider->Grid_GetColumnCount()!=columns) return 13;
        ComPtr<IAvnAutomationPeer> container;
        if(item->GridItem_GetContainingGrid(container.getPPV())!=S_OK) return 14;
        if(container.getRaw()!=grid.getRaw()) return 14;
        Class elementClass=NSClassFromString(@"AvnAccessibilityElement");
        if(elementClass==Nil) return 20;
        NSAccessibilityElement* nativeGrid=[elementClass acquire:grid];
        NSAccessibilityElement* nativeCell=[elementClass acquire:cell];
        if([nativeGrid accessibilityRowCount]!=rows || [nativeGrid accessibilityColumnCount]!=columns) return 21;
        if([nativeCell accessibilityParent]!=nativeGrid) return 22;
        NSRange rowRange=row>=0?NSMakeRange(row,1):NSMakeRange(NSNotFound,0);
        NSRange columnRange=column>=0?NSMakeRange(column,1):NSMakeRange(NSNotFound,0);
        if(!NSEqualRanges([nativeCell accessibilityRowIndexRange],rowRange) || !NSEqualRanges([nativeCell accessibilityColumnIndexRange],columnRange)) return 23;
        if([nativeGrid accessibilityCellForColumn:columns row:0]!=nil || [nativeGrid accessibilityCellForColumn:-1 row:0]!=nil) return 24;
        NSArray* nativeChildren=[nativeGrid accessibilityChildren];
        for(int r=0;r<rows;r++) for(int c=0;c<columns;c++)
        {
            ComPtr<IAvnAutomationPeer> found;
            if(provider->Grid_GetItem(r,c,found.getPPV())!=S_OK) return 25;
            NSAccessibilityElement* nativeFound=[elementClass acquire:found];
            if([nativeGrid accessibilityCellForColumn:c row:r]!=nativeFound) return 25;
            if(nativeChildren[r*columns+c]!=nativeFound) return 26;
        }
        if(row>=0 && column>=0 && [nativeGrid accessibilityCellForColumn:column row:row]!=nativeCell) return 27;
        ComPtr<IAvnAutomationPeerArray> visible(provider->Grid_GetVisibleCells(),true),selected(provider->Grid_GetSelectedCells(),true);
        if(!SameArray([nativeGrid accessibilityVisibleCells],visible) || !SameArray([nativeGrid accessibilitySelectedCells],selected) ||
            !SameArray([nativeGrid accessibilitySelectedChildren],selected)) return 28;
        if(child!=nullptr)
        {
            NSAccessibilityElement* nativeChild=[elementClass acquire:child];
            if([nativeChild accessibilityParent]!=nativeCell) return 29;
            if(focus) { [nativeChild setAccessibilityFocused:YES]; if(![nativeChild isAccessibilityFocused]) return 30; }
            if(press && ![nativeChild accessibilityPerformPress]) return 31;
        }
        ComPtr<IAvnAutomationNode> node(cell->GetNode(),true);
        if(node==nullptr) return 32;
        auto before=node->AddRef();node->Release();
        if(row>=0 && column>=0) for(int i=0;i<64;i++)
            if([nativeGrid accessibilityCellForColumn:column row:row]!=nativeCell) return 33;
        auto after=node->AddRef();node->Release();
        if(before!=after) return 34;
        return 0;
    }
}

// Public runtime inspection only: prove which loaded image owns the real selector.
extern "C" __attribute__((visibility("default"))) const char* FreeXGridBridgeImagePath()
{
    Class element=NSClassFromString(@"AvnAccessibilityElement");
    Method method=class_getInstanceMethod(element,@selector(accessibilityCellForColumn:row:));
    Dl_info info={};
    return method!=nullptr && dladdr(reinterpret_cast<const void*>(method_getImplementation(method)),&info) ? info.dli_fname : nullptr;
}

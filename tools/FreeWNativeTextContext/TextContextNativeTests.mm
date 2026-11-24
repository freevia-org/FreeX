#import <AppKit/AppKit.h>
#include <dlfcn.h>
#include <stdint.h>
#include <limits.h>

@protocol FreeWTextContextView <NSTextInputClient>
- (instancetype)initWithParent:(void*)parent;
- (void)setText:(NSString*)text;
- (void)setSelection:(int)start :(int)end;
@end

static NSMutableArray<NSString*>* failures;
static int rangeFailures;
static int selectionFailures;
static int caseCount;

static void CheckRange(id<FreeWTextContextView> view, NSString* name, NSRange proposed,
    NSString* expected, NSRange expectedRange, bool outputPointer = true)
{
    caseCount++;
    // The output parameter deliberately contains an unrelated range. Reading it as an input is
    // the pinned upstream defect; its exception must be observed safely, not crash the test host.
    NSRange actual = NSMakeRange(99, 77);
    @try
    {
        NSAttributedString* result = [view attributedSubstringForProposedRange:proposed
            actualRange:outputPointer ? &actual : nullptr];
        bool textMatches = expected == nil ? result == nil : [result.string isEqualToString:expected];
        bool rangeMatches = !outputPointer || NSEqualRanges(actual, expectedRange);
        if (!textMatches || !rangeMatches)
        {
            rangeFailures++;
            [failures addObject:[name stringByAppendingString:@": wrong substring or output range"]];
        }
    }
    @catch (NSException* exception)
    {
        rangeFailures++;
        [failures addObject:[NSString stringWithFormat:@"%@: %@", name, exception.name]];
    }
}

static void CheckSelection(id<FreeWTextContextView> view, NSString* name, int anchor, int caret, NSRange expected)
{
    caseCount++;
    @try
    {
        [view setSelection:anchor :caret];
        if (!NSEqualRanges(view.selectedRange, expected))
        {
            selectionFailures++;
            [failures addObject:[name stringByAppendingString:@": invalid Cocoa selection range"]];
        }
    }
    @catch (NSException* exception)
    {
        selectionFailures++;
        [failures addObject:[NSString stringWithFormat:@"%@: %@", name, exception.name]];
    }
}

int main(int argc, char** argv)
{
    if (argc != 2 && argc != 3)
    {
        fprintf(stderr, "Usage: TextContextNativeTests <explicit native library path> [JSON report path]\n");
        return 2;
    }
    void* library = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (!library)
    {
        fprintf(stderr, "Could not load the requested test library: %s\n", dlerror());
        return 2;
    }
    int exitCode = 2;
    @autoreleasepool
    {
        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
        Class viewClass = NSClassFromString(@"AvnView");
        if (!viewClass)
        {
            fprintf(stderr, "Requested library did not register AvnView.\n");
            dlclose(library);
            return 2;
        }
        failures = [NSMutableArray new];
        id<FreeWTextContextView> view = (id<FreeWTextContextView>)[viewClass alloc];
        view = [view initWithParent:nullptr];
        if (!view)
        {
            fprintf(stderr, "Could not create the native text-context view.\n");
            dlclose(library);
            return 2;
        }

        [view setText:@"abcd"];
        CheckRange(view, @"exact-output", NSMakeRange(1, 2), @"bc", NSMakeRange(1, 2));
        CheckRange(view, @"null-output", NSMakeRange(1, 2), @"bc", NSMakeRange(1, 2), false);
        CheckRange(view, @"clipped-end", NSMakeRange(2, 99), @"cd", NSMakeRange(2, 2));
        CheckRange(view, @"clipped-null-output", NSMakeRange(2, 99), @"cd", NSMakeRange(2, 2), false);
        CheckRange(view, @"unsigned-length-overflow", NSMakeRange(1, NSUIntegerMax), @"bcd", NSMakeRange(1, 3));
        CheckRange(view, @"outside", NSMakeRange(20, 1), nil, NSMakeRange(NSNotFound, 0));
        CheckRange(view, @"not-found", NSMakeRange(NSNotFound, NSUIntegerMax), nil, NSMakeRange(NSNotFound, 0));
        CheckRange(view, @"unsigned-location-overflow", NSMakeRange(NSUIntegerMax, 2), nil, NSMakeRange(NSNotFound, 0));
        CheckRange(view, @"empty-at-end", NSMakeRange(4, 0), @"", NSMakeRange(4, 0));
        CheckRange(view, @"outside-at-end", NSMakeRange(4, 1), nil, NSMakeRange(NSNotFound, 0));
        CheckSelection(view, @"forward", 1, 3, NSMakeRange(1, 2));
        CheckSelection(view, @"backward", 3, 1, NSMakeRange(1, 2));
        CheckSelection(view, @"negative-endpoint", -2, 3, NSMakeRange(0, 3));
        CheckSelection(view, @"large-endpoint", 1, INT_MAX, NSMakeRange(1, 3));
        CheckSelection(view, @"both-outside", INT_MAX, -20, NSMakeRange(0, 4));

        [view setText:@"a😀e\u0301z"];
        CheckRange(view, @"utf16-surrogate-pair", NSMakeRange(1, 2), @"😀", NSMakeRange(1, 2));
        CheckRange(view, @"combining-sequence", NSMakeRange(3, 2), @"e\u0301", NSMakeRange(3, 2));
        [view setText:@""];
        CheckRange(view, @"empty-storage", NSMakeRange(0, 0), @"", NSMakeRange(0, 0));
        CheckSelection(view, @"empty-selection", INT_MAX, -1, NSMakeRange(0, 0));

        auto marker = reinterpret_cast<int32_t (*)(void)>(dlsym(library, "FreeW_AvaloniaNative_TextContext_V1"));
        int version = marker ? marker() : 0;
        NSDictionary* report = @{
            @"contractVersion": @(version), @"cases": @(caseCount),
            @"rangeFailures": @(rangeFailures), @"selectionFailures": @(selectionFailures),
            @"failures": failures,
        };
        NSData* json = [NSJSONSerialization dataWithJSONObject:report options:NSJSONWritingPrettyPrinted error:nil];
        if (argc == 3 && ![json writeToFile:[NSString stringWithUTF8String:argv[2]] options:NSDataWritingAtomic error:nil])
        {
            fprintf(stderr, "Could not write the native regression report.\n");
            return 2;
        }
        fwrite(json.bytes, 1, json.length, stdout);
        fputc('\n', stdout);
        exitCode = rangeFailures == 0 && selectionFailures == 0 && version == 1 ? 0 : 1;
        view = nil;
        failures = nil;
    }
    dlclose(library);
    return exitCode;
}

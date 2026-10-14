package com.example.jamkkaebi.purification.service;

/**
 * 보드 생성기의 난수 — SplitMix64..
 */
final class SplitMix64 {

    private long state;

    SplitMix64(long seed) {
        this.state = seed;
    }

    long next() {
        state += 0x9E3779B97F4A7C15L;
        long z = state;
        z = (z ^ (z >>> 30)) * 0xBF58476D1CE4E5B9L;
        z = (z ^ (z >>> 27)) * 0x94D049BB133111EBL;
        return z ^ (z >>> 31);
    }

    /**
     * {@code [0, bound)} 균등
     */
    int nextInt(int bound) {
        if (bound <= 0) {
            throw new IllegalArgumentException("bound 는 1 이상이어야 합니다: " + bound);
        }
        long threshold = Long.remainderUnsigned(-(long) bound, bound); // == 2^64 mod bound
        long value;
        do {
            value = next();
        } while (Long.compareUnsigned(value, threshold) < 0);
        return (int) Long.remainderUnsigned(value, bound);
    }
}

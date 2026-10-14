package com.example.jamkkaebi.relic.repository;

import com.example.jamkkaebi.relic.domain.Era;

/**
 * 한 사용자가 한 시대에서 완료한 유물 수.
 */
public record EraCompletion(Long userId, Era era, long count) {
}

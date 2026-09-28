package com.example.jamkkaebi.workbench.dto.response;

import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 건물의 다음 레벨.
 *
 * @param cost       필요한 시대의 결정.
 * @param affordable 지금 강화할 수 있는가.
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record NextLevelView(int level, Integer cost, boolean affordable) {

    public static NextLevelView of(int level, Integer cost, int balance) {
        return new NextLevelView(level, cost, cost != null && balance >= cost);
    }
}

package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.response.FieldError;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import com.example.jamkkaebi.user.service.FriendCodeFormat;
import org.springframework.stereotype.Component;

import java.util.Arrays;
import java.util.Collection;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.function.Function;
import java.util.stream.Collectors;

@Component
public class FriendUsers {

    private final UserRepository userRepository;

    public FriendUsers(UserRepository userRepository) {
        this.userRepository = userRepository;
    }

    // 요청한 본인
    public User me(String friendCode) {
        return userRepository.findByFriendCode(friendCode)
                .orElseThrow(() -> new BusinessException(ErrorCode.USER_NOT_FOUND));
    }

    // 입력한 친구 코드의 사용자
    public Optional<User> findByCode(String rawFriendCode) {
        return FriendCodeFormat.normalize(rawFriendCode).flatMap(userRepository::findByFriendCode);
    }

    public Map<Long, User> findAll(Collection<Long> userIds) {
        if (userIds.isEmpty()) {
            return Map.of();
        }
        return userRepository.findAllById(userIds).stream()
                .collect(Collectors.toMap(User::getId, Function.identity()));
    }

    // 사용자 행을 잠근다
    public void lock(Long... userIds) {
        List<Long> ordered = Arrays.stream(userIds).distinct().sorted().toList();
        userRepository.lockAllByIds(ordered);
    }

    // 정규화된 친구 코드
    public static String requireFormat(String rawFriendCode) {
        return FriendCodeFormat.normalize(rawFriendCode)
                .orElseThrow(() -> invalidFriendCode("8자리 친구 코드여야 합니다."));
    }

    public static BusinessException invalidFriendCode(String reason) {
        return new BusinessException(ErrorCode.INVALID_INPUT, List.of(new FieldError("friendCode", reason)));
    }
}

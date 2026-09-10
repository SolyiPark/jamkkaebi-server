package com.example.jamkkaebi.user.repository;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import com.example.jamkkaebi.user.domain.User;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.Optional;

public interface UserRepository extends JpaRepository<User, Long> {

    /** 계정 매칭 기준(provider + providerUserId)으로 사용자를 찾는다. */
    Optional<User> findByProviderAndProviderUserId(AuthProvider provider, String providerUserId);

    /** JWT 의 sub 로 들어 있는 친구 코드로 사용자를 찾는다. */
    Optional<User> findByFriendCode(String friendCode);

    boolean existsByFriendCode(String friendCode);
}
